extern alias client;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The fourteen declarations <c>plan_shim.md</c> Phase 5 (lane 5d) took out of
/// C++: eleven movevars, <c>phys_pushscale</c>, <c>sv_soundemitter_trace</c> and
/// <c>english</c>.
///
/// <para>
/// <b>The expected values below are typed out from the C++, not generated from
/// the C#.</b> <c>SourceSharp.Server/MoveVars.cs</c> and
/// <c>SourceSharp.Client/MoveVars.cs</c> were written by reading the
/// declarations they replace, which makes them right at the moment of the move
/// and says nothing about any edit afterwards. A table re-derived from the same
/// C# would be a check that cannot fail. This one is an independent
/// restatement, read off <c>movevars_shared.cpp</c>,
/// <c>takedamageinfo_library.cpp</c>, <c>sound_emitter.cpp</c> and
/// <c>shell_hud_framework.cpp</c> at <c>c943fed92</c>.
/// </para>
///
/// <para>
/// <b>Thirteen of the fourteen must be declared on BOTH sides.</b> They are
/// <c>FCVAR_REPLICATED</c> and they came out of files compiled into both DLLs,
/// so each name was declared twice before the move and has to be declared twice
/// after it. A replicated ConVar with only one half is a prediction bug that no
/// compile step, no link step and no single-player run can see: the client's
/// copy simply never tracks the server. <c>english</c> is the fourteenth and is
/// client-only, exactly as it was in C++.
/// </para>
///
/// <para>
/// <b>The engine is absent here</b>, so every registration is refused and each
/// convar binds with an id of -1 to the value it declared. That is the half
/// worth testing without a game: a refused convar keeps what it declared.
/// Whether the ENGINE agrees is the live gate's question, and for these the
/// question that matters there is ordering — see <c>docs/convar-ledger.md</c>.
/// </para>
/// </summary>
public class MoveVarsTests
{
    private readonly record struct Moved(
        string Name, string DefaultValue, ConVarFlags Flags, string Help, bool BothSides);

    private const ConVarFlags VehicleFlags =
        ConVarFlags.Cheat | ConVarFlags.Notify | ConVarFlags.Replicated;

    /// <summary>
    /// The fourteen, as the C++ declared them.
    ///
    /// <para>
    /// The defaults keep their C++ spelling — "7.0" and "1.0" rather than "7"
    /// and "1" — because the console prints the default back verbatim and
    /// tidying one would change what a player sees.
    /// </para>
    /// </summary>
    private static readonly Moved[] Declarations =
    {
        // movevars_shared.cpp, the four movement knobs with no reader left.
        new("sv_backspeed", "0.6", ConVarFlags.Archive | ConVarFlags.Replicated,
            "How much to slow down backwards motion", true),
        new("sv_footsteps", "1", ConVarFlags.Notify | ConVarFlags.Replicated,
            "Play footstep sound for players", true),
        new("sv_wateraccelerate", "10", ConVarFlags.Notify | ConVarFlags.Replicated,
            "", true),
        new("sv_waterdist", "12", ConVarFlags.Replicated,
            "Vertical view fixup when eyes are near water plane.", true),

        // movevars_shared.cpp, the seven vehicle view-damping knobs.
        new("r_VehicleViewDampen", "1", VehicleFlags, "", true),
        new("r_JeepViewDampenFreq", "7.0", VehicleFlags, "", true),
        new("r_JeepViewDampenDamp", "1.0", VehicleFlags, "", true),
        new("r_JeepViewZHeight", "10.0", VehicleFlags, "", true),
        new("r_AirboatViewDampenFreq", "7.0", VehicleFlags, "", true),
        new("r_AirboatViewDampenDamp", "1.0", VehicleFlags, "", true),
        new("r_AirboatViewZHeight", "0.0", VehicleFlags, "", true),

        // takedamageinfo_library.cpp. No help text in the C++ either.
        new("phys_pushscale", "1", ConVarFlags.Replicated, "", true),

        // sound_emitter.cpp. The trailing newline is stock's and is kept: it is
        // what the console prints.
        new("sv_soundemitter_trace", "0", ConVarFlags.Replicated,
            "Show all EmitSound calls including their symbolic name and the actual wave file they resolved to\n",
            true),

        // shell_hud_framework.cpp, and the only one of the fourteen that is
        // client-only. closecaption, declared on the line above it in C++,
        // STAYED: the engine resolves that name itself.
        new("english", "1", ConVarFlags.UserInfo,
            "If set to 1, running the english language set of assets.", false),
    };

    private static readonly Assembly ServerAssembly =
        typeof(SourceSharp.Server.SvBackspeedConVar).Assembly;

    private static readonly Assembly ClientAssembly =
        typeof(client::SourceSharp.Client.SvBackspeedConVar).Assembly;

    private static IReadOnlyDictionary<string, ConVarAttribute> DeclarationsIn(Assembly assembly)
    {
        var found = new Dictionary<string, ConVarAttribute>(StringComparer.OrdinalIgnoreCase);

        foreach (var type in assembly.GetTypes())
        {
            var attr = SideAttribute.Get<ConVarAttribute>(type);
            if (attr is not null)
                found[attr.Name] = attr;
        }

        return found;
    }

    private static List<(string Side, ConVarAttribute Attr)> EveryDeclarationOf(string name)
    {
        var all = new List<(string, ConVarAttribute)>();

        if (DeclarationsIn(ServerAssembly).TryGetValue(name, out var s))
            all.Add(("SourceSharp.Server", s));
        if (DeclarationsIn(ClientAssembly).TryGetValue(name, out var c))
            all.Add(("SourceSharp.Client", c));

        Assert.True(all.Count > 0,
            name + " is declared by neither SourceSharp.Server nor SourceSharp.Client. "
                 + "Its C++ declaration is gone, so nothing declares it at all and the "
                 + "setting has silently ceased to exist.");

        return all;
    }

    private static Moved Expected(string name) => Declarations.Single(k => k.Name == name);

    public static TheoryData<string> EveryMovedName()
    {
        var data = new TheoryData<string>();
        foreach (var d in Declarations)
            data.Add(d.Name);

        return data;
    }

    //-------------------------------------------------------------------------
    // One behaviour per fact.
    //-------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(EveryMovedName))]
    public void EveryMovedNameIsDeclaredInManagedCode(string name)
    {
        foreach (var (_, attr) in EveryDeclarationOf(name))
            Assert.Equal(name, attr.Name);
    }

    [Theory]
    [MemberData(nameof(EveryMovedName))]
    public void EveryMovedNameKeptItsCppDefault(string name)
    {
        foreach (var (side, attr) in EveryDeclarationOf(name))
            Assert.Equal(Expected(name).DefaultValue + " (" + side + ")",
                         attr.DefaultValue + " (" + side + ")");
    }

    [Theory]
    [MemberData(nameof(EveryMovedName))]
    public void EveryMovedNameKeptItsCppFlags(string name)
    {
        foreach (var (side, attr) in EveryDeclarationOf(name))
            Assert.Equal(Expected(name).Flags + " (" + side + ")",
                         attr.Flags + " (" + side + ")");
    }

    [Theory]
    [MemberData(nameof(EveryMovedName))]
    public void EveryMovedNameKeptItsCppHelpText(string name)
    {
        foreach (var (side, attr) in EveryDeclarationOf(name))
            Assert.Equal(Expected(name).Help + " (" + side + ")",
                         attr.Help + " (" + side + ")");
    }

    [Theory]
    [MemberData(nameof(EveryMovedName))]
    public void AReplicatedNameIsDeclaredOnBothSides(string name)
    {
        var expected = Expected(name);
        if (!expected.BothSides)
            return;

        var sides = EveryDeclarationOf(name).Select(d => d.Side).ToList();

        Assert.True(sides.Contains("SourceSharp.Server"),
            name + " is FCVAR_REPLICATED and came out of a file compiled into BOTH "
                 + "DLLs, so the server must declare it. A replicated convar with one "
                 + "half missing never tracks the other side, and nothing says so.");
        Assert.True(sides.Contains("SourceSharp.Client"),
            name + " is FCVAR_REPLICATED and came out of a file compiled into BOTH "
                 + "DLLs, so the client must declare it too.");
    }

    [Fact]
    public void TheClientOnlyNameIsNotDeclaredByTheServer()
    {
        // The converse of the fact above, and the reason it is worth having: a
        // convar registered from two DLLs when only one side ever had it is a
        // second setting nobody meant, and Source says nothing about it.
        Assert.Single(EveryDeclarationOf("english"));
        Assert.Equal("SourceSharp.Client", EveryDeclarationOf("english")[0].Side);
    }

    [Fact]
    public void EveryReplicatedDeclarationCarriesTheReplicatedFlag()
    {
        // The flag is the whole mechanism: the engine replicates by FLAG and not
        // by name -- none of these thirteen is in engine.so at all -- so a
        // declaration that loses it is a knob the client stops tracking.
        foreach (var d in Declarations.Where(x => x.BothSides))
        {
            foreach (var (side, attr) in EveryDeclarationOf(d.Name))
            {
                Assert.True(attr.Flags.HasFlag(ConVarFlags.Replicated),
                    d.Name + " lost FCVAR_REPLICATED on " + side + ".");
            }
        }
    }

    [Fact]
    public void TheTwoSidesAgreeOnEveryReplicatedDeclaration()
    {
        // Name by name, default by default, flag by flag -- rather than trusting
        // that two lists written on the same afternoon agree. A one-sided edit
        // to either file is exactly the silent failure this file exists for.
        foreach (var d in Declarations.Where(x => x.BothSides))
        {
            var server = DeclarationsIn(ServerAssembly)[d.Name];
            var client = DeclarationsIn(ClientAssembly)[d.Name];

            Assert.Equal(server.DefaultValue, client.DefaultValue);
            Assert.Equal(server.Flags, client.Flags);
            Assert.Equal(server.Help, client.Help);
        }
    }

    [Fact]
    public void TheRegistryBindsEveryMovedNameToItsDeclaredDefault()
    {
        var registered = BothSidesConVars.Registered(ServerAssembly, ClientAssembly);

        foreach (var d in Declarations)
        {
            var bound = registered.Where(
                c => string.Equals(c.Name, d.Name, StringComparison.OrdinalIgnoreCase)).ToList();

            Assert.True(bound.Count > 0, d.Name + " reached no registry at all.");

            foreach (var one in bound)
                Assert.Equal(d.DefaultValue, one.DefaultValue);
        }
    }

    //-------------------------------------------------------------------------
    // And the C++ they came out of, so that "it is declared in C# now" cannot be
    // satisfied while a second declaration is still in C++. Two ConVars under
    // one name is neither a compile error nor a link error, and neither the
    // console nor the game says which one it read.
    //-------------------------------------------------------------------------

    public static TheoryData<string, string> EveryMovedNameAndItsOldFile()
    {
        var data = new TheoryData<string, string>();
        const string shared = "src/sourcesharp/game/shared/";
        foreach (var d in Declarations)
        {
            // sv_soundemitter_trace's old file, sound_emitter.cpp, left the
            // repository with the client's emit path (SourceSharp.Client.Sounds);
            // TheTraceKnobHasNoNativeDeclarationAnywhere holds it over every
            // native source instead.
            if (d.Name == "sv_soundemitter_trace")
                continue;

            data.Add(d.Name, d.Name switch
            {
                "phys_pushscale" => shared + "takedamageinfo_library.cpp",
                "english" => "src/sourcesharp/game/client/shell_hud_framework.cpp",
                _ => shared + "movevars_shared.cpp",
            });
        }

        return data;
    }

    [Fact]
    public void TheTraceKnobHasNoNativeDeclarationAnywhere()
    {
        // Every native source of this repository's three shim directories: the
        // knob's old home is gone, so no one file is the place to look.
        string[] roots = ["src/client/native", "src/server/native", "src/shared/native"];
        int read = 0;
        foreach (string root in roots)
        {
            string dir = RepoLayout.Repo(root.Split('/'));
            Assert.True(Directory.Exists(dir), dir + " is not in the tree");
            foreach (string path in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
            {
                if (!path.EndsWith(".cpp", StringComparison.Ordinal) && !path.EndsWith(".h", StringComparison.Ordinal))
                    continue;
                string text = File.ReadAllText(path);
                read++;
                Assert.DoesNotContain("ConVar sv_soundemitter_trace(", text);
                Assert.DoesNotContain("ConVar sv_soundemitter_trace (", text);
            }
        }

        Assert.True(read > 300, $"only {read} native sources were read");
    }

    [Theory]
    [MemberData(nameof(EveryMovedNameAndItsOldFile))]
    public void NoNativeDeclarationWasLeftBehind(string name, string file)
    {
        var path = Path.Combine(RepoLayout.SdkTree, file);
        Assert.True(File.Exists(path), path + " is not in the tree");

        var text = File.ReadAllText(path);

        // The CONSTRUCTOR, not the name: every one of these files still mentions
        // the name in its banner saying where the declaration went, and a needle
        // a comment can satisfy is a needle that stopped being about the code.
        Assert.DoesNotContain("ConVar " + name + "(", text);
        Assert.DoesNotContain("ConVar\t" + name + "\t", text);
        Assert.DoesNotContain("ConVar " + name + " (", text);
    }

    [Fact]
    public void TheHeaderNoLongerExternsTheElevenMovevars()
    {
        var path = Path.Combine(RepoLayout.SdkTree, "src/game/shared/movevars_shared.h");
        Assert.True(File.Exists(path), path + " is not in the tree");

        var text = File.ReadAllText(path);

        foreach (var d in Declarations)
        {
            if (d.Name is "phys_pushscale" or "sv_soundemitter_trace" or "english")
                continue;

            Assert.DoesNotContain("extern ConVar " + d.Name + ";", text);
        }
    }

    [Fact]
    public void TheHeaderStillExternsTheSeventeenThatDidNotMove()
    {
        // The other half, and the one that makes the fact above mean something:
        // emptying the header would satisfy every "no longer externs" assertion.
        var path = Path.Combine(RepoLayout.SdkTree, "src/game/shared/movevars_shared.h");
        var text = File.ReadAllText(path);

        string[] stayed =
        {
            "sv_gravity", "sv_stopspeed", "sv_noclipaccelerate", "sv_noclipspeed",
            "sv_maxspeed", "sv_accelerate", "sv_airaccelerate", "sv_waterfriction",
            "sv_rollspeed", "sv_rollangle", "sv_friction", "sv_bounce",
            "sv_maxvelocity", "sv_stepsize", "sv_skyname", "sv_specaccelerate",
            "sv_specspeed", "sv_specnoclip",
        };

        foreach (var name in stayed)
        {
            Assert.Contains("extern ConVar " + name + ";", text);
        }
    }

    [Fact]
    public void CloseCaptionStayedNativeBesideTheOneThatWent()
    {
        // english and closecaption were declared on consecutive lines, both
        // FCVAR_USERINFO, and only one of them moved. The difference is that the
        // shipped engine.so resolves closecaption BY NAME and never mentions
        // english -- so this pins the asymmetry rather than leaving it to the
        // ledger's prose.
        var path = Path.Combine(RepoLayout.SdkTree,
            "src/sourcesharp/game/client/shell_hud_framework.cpp");
        var text = File.ReadAllText(path);

        Assert.Contains("ConVar closecaption( \"closecaption\"", text);
        Assert.DoesNotContain("ConVar english( \"english\"", text);

        Assert.False(DeclarationsIn(ClientAssembly).ContainsKey("closecaption"),
            "closecaption is declared in managed code as well as in C++. Two "
          + "ConVars under one name is neither a compile error nor a link error.");
        Assert.False(DeclarationsIn(ServerAssembly).ContainsKey("closecaption"),
            "closecaption is declared by the server, which never declared it.");
    }
}
