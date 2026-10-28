extern alias client;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The fifty-six ConVar declarations the shim handed to managed code, held
/// against the C++ they came from.
///
/// <para>
/// <b>What moved and what did not.</b> plan_entity.md §6 keeps ConVar
/// REGISTRATION native — the engine owns one registry per process and both game
/// DLLs put their convars in it — but adds the rule that no ConVar's default or
/// semantics is declared natively. A <c>ConVar foo( "foo", "1", 0, "help" )</c>
/// line in C++ IS the tuning value: the name, the default, the flags and the
/// help text are the whole of it, and while it sat in a shim <c>.cpp</c> no
/// managed code could read it, set it or list it. The declarations moved; the
/// rules that read them did not, because they are asked per blood effect, per
/// candidate and per particle.
/// </para>
///
/// <para>
/// <b>Why the expected values are written out here rather than generated.</b>
/// The declarations in <c>SourceSharp.Server/ConVars.cs</c> and
/// <c>SourceSharp.Client/ConVars.cs</c> WERE generated, from the C++ that used
/// to hold them — which makes them right at the moment of the move and says
/// nothing about any edit afterwards. Re-generating them here from the same
/// place would be a check that cannot fail. The table below is an independent
/// restatement, read off the C++ at <c>879d2dcfc</c> and typed out, and it has
/// already earned its place: it caught an invented help string on
/// <c>hud_drawhistory_time</c> ("Time to draw the pickup history for", where
/// the C++ says "Seconds the pickup history stays up. Unused here — the history
/// HUD is hidden") before that reached a commit.
/// </para>
///
/// <para>
/// <b>The engine is absent here</b>, so every registration is refused and each
/// convar binds with an id of -1 to the value it declared. That is the half
/// worth testing: a refused convar keeps what it declared, which is what makes
/// a declaration checkable without a running game. What it does NOT check is
/// that the engine agrees — that is the lane's live gate, which reads all
/// fifty-six back out of a running game's own registry.
/// </para>
/// </summary>
public class PolicyKnobDeclarationTests
{
    /// <summary>One row of the C++ these came from.</summary>
    private readonly record struct Knob(
        string Name, string DefaultValue, ConVarFlags Flags, string Help);

    /// <summary>
    /// Every declaration this lane moved, as the C++ declared it.
    ///
    /// <para>
    /// Three of the snow defaults carry an 'f' INSIDE the string — "384.0f",
    /// "512.0f", "8192.0f". That is stock's and is deliberately preserved: the
    /// engine parses a default with atof, which stops at the 'f' and yields the
    /// same number, but tidying it would change a default the console prints.
    /// </para>
    /// </summary>
    private static readonly Knob[] Moved =
    {
        new("sv_strict_notarget", "0", ConVarFlags.None, "If set, notarget will cause entities to never think they are in the pvs"),
        new("ai_post_frame_navigation", "0", ConVarFlags.None, ""),
        new("ai_shot_stats", "0", ConVarFlags.None, ""),
        new("soundscape_fadetime", "3.0", ConVarFlags.Cheat, "Time to crossfade sound effects between soundscapes"),
        new("cl_winddir", "0", ConVarFlags.Cheat, "Weather effects wind direction angle"),
        new("cl_windspeed", "0", ConVarFlags.Cheat, "Weather effects wind speed scalar"),
        new("r_RainSplashPercentage", "20", ConVarFlags.Cheat, ""),
        new("r_RainHack", "0", ConVarFlags.Cheat, ""),
        new("r_RainRadius", "1500", ConVarFlags.Cheat, ""),
        new("r_RainSideVel", "130", ConVarFlags.Cheat, "How much sideways velocity rain gets."),
        new("r_RainSimulate", "1", ConVarFlags.Cheat, "Enable/disable rain simulation."),
        new("r_DrawRain", "1", ConVarFlags.Cheat, "Enable/disable rain rendering."),
        new("r_RainProfile", "0", ConVarFlags.Cheat, "Enable/disable rain profiling."),
        new("r_SnowEnable", "1", ConVarFlags.Cheat, "Snow Enable"),
        new("r_SnowParticles", "500", ConVarFlags.Cheat, "Snow."),
        new("r_SnowInsideRadius", "256", ConVarFlags.Cheat, "Snow."),
        new("r_SnowOutsideRadius", "1024", ConVarFlags.Cheat, "Snow."),
        new("r_SnowSpeedScale", "1", ConVarFlags.Cheat, "Snow."),
        new("r_SnowPosScale", "1", ConVarFlags.Cheat, "Snow."),
        new("r_SnowFallSpeed", "1.5", ConVarFlags.Cheat, "Snow fall speed scale."),
        new("r_SnowWindScale", "0.0035", ConVarFlags.Cheat, "Snow."),
        new("r_SnowDebugBox", "0", ConVarFlags.Cheat, "Snow Debug Boxes."),
        new("r_SnowZoomOffset", "384.0f", ConVarFlags.Cheat, "Snow."),
        new("r_SnowZoomRadius", "512.0f", ConVarFlags.Cheat, "Snow."),
        new("r_SnowStartAlpha", "25", ConVarFlags.Cheat, "Snow."),
        new("r_SnowEndAlpha", "255", ConVarFlags.Cheat, "Snow."),
        new("r_SnowColorRed", "150", ConVarFlags.Cheat, "Snow."),
        new("r_SnowColorGreen", "175", ConVarFlags.Cheat, "Snow."),
        new("r_SnowColorBlue", "200", ConVarFlags.Cheat, "Snow."),
        new("r_SnowStartSize", "1", ConVarFlags.Cheat, "Snow."),
        new("r_SnowEndSize", "0", ConVarFlags.Cheat, "Snow."),
        new("r_SnowRayLength", "8192.0f", ConVarFlags.Cheat, "Snow."),
        new("r_SnowRayRadius", "256", ConVarFlags.Cheat, "Snow."),
        new("r_SnowRayEnable", "1", ConVarFlags.Cheat, "Snow."),
        new("r_rainalpha", "0.4", ConVarFlags.Cheat, ""),
        new("r_rainalphapow", "0.8", ConVarFlags.Cheat, ""),
        new("hud_drawhistory_time", "5", ConVarFlags.None, "Seconds the pickup history stays up. Unused here — the history HUD is hidden."),
        // Moved by lane 4c-render, plan_shim.md §6. Its default is 1 and not
        // stock's 0, which predates the move: switching is always immediate
        // here because there is no selection UI to drive.
        new("hud_fastswitch", "1", ConVarFlags.Archive, "Weapon switching is always immediate in this mod; kept for config compatibility."),

        // plan_shim.md §6, lane 4b-effects: the thirteen the effects and
        // temp-entity shim still declared in C++. Read off the C++ at f225149e9
        // and typed out, like every row above.
        new("r_raindensity", "0.001", ConVarFlags.Cheat, ""),
        new("r_rainwidth", "0.5", ConVarFlags.Cheat, ""),
        new("r_rainlength", "0.1f", ConVarFlags.Cheat, ""),
        new("r_rainspeed", "600.0f", ConVarFlags.Cheat, ""),
        new("cl_spraydisable", "0", ConVarFlags.ClientDll | ConVarFlags.Archive, "Disable player sprays."),
        new("cl_sporeclipdistance", "512", ConVarFlags.Cheat | ConVarFlags.ClientDll, ""),
        new("pointworldtext_rainbowspeed_time", "100.0f", ConVarFlags.None, ""),
        new("pointworldtext_rainbowspeed_char", "15.0f", ConVarFlags.None, ""),
        new("fx_drawimpactdebris", "1", ConVarFlags.DevelopmentOnly, "Draw impact debris effects."),
        new("fx_drawimpactdust", "1", ConVarFlags.DevelopmentOnly, "Draw impact dust effects."),
        new("r_drawflecks", "1", ConVarFlags.AllowedInCompetitive, ""),
        new("cl_new_impact_effects", "0", ConVarFlags.None, ""),
    };

    /// <summary>
    /// Names the engine itself defines, which these files once declared too —
    /// four from game/shared/util_shared_library.cpp, sv_alternateticks,
    /// sv_unlockedchapters and mat_reduceparticles. The live table shows each
    /// with neither game-DLL flag, so a managed declaration only aliased the
    /// engine's (convar_ownership.h), and the registry now refuses one at
    /// startup. Native readers resolve the engine's by name.
    /// </summary>
    private static readonly string[] EngineOwned =
    {
        "violence_hblood", "violence_hgibs", "violence_ablood", "violence_agibs",
        "sv_alternateticks", "sv_unlockedchapters", "mat_reduceparticles", "mod_load_showstall",
    };

    /// <summary>
    /// The two that still cannot come, and stay in C++. Each has its C++
    /// declaration still standing, so declaring one here as well would leave
    /// the game with two convars of one name — which is neither a compile error
    /// nor a link error, and nothing says which one the game read.
    ///
    /// <para>
    /// This list held FOUR. <c>ai_LOS_mode</c> and
    /// <c>sv_vehicle_autoaim_scale</c> left it in Phase 1b, and the reason is a
    /// measurement rather than a re-reading: both were blocked by an
    /// <c>extern ConVar</c> in <c>game/server/baseentity.cpp</c>, and that
    /// translation unit is not compiled into <c>server_sourcesharp</c> — there is no
    /// <c>baseentity.o</c> on the server link line for the extern to live in.
    /// Both are now declared in <see cref="SourceSharp.Server"/> and their C++
    /// declarations are deleted; the two facts below pin each half.
    /// </para>
    ///
    /// <para>
    /// ONE REMAINS, AND IT WAS TWO. <c>default_fov</c> is still imported by
    /// <c>view.o</c> (<c>game/client/view.cpp</c>, one importer on the client
    /// link line), so the link really does need the native object.
    /// </para>
    ///
    /// <para>
    /// <c>hud_fastswitch</c> LEFT THIS LIST in lane 4c-render and is in
    /// <see cref="Moved"/> now. It was never blocked by the link the way
    /// <c>default_fov</c> is: its stock reader <c>weapon_parse.cpp</c> had
    /// already dropped off both link lines, leaving one definer
    /// (<c>shell_hud.o</c>) and zero importers, so it was blocked by file
    /// ownership — the client shim file it lived in belonged to another lane.
    /// This list is for the LINK-blocked ones, which is why the distinction is
    /// worth keeping: an ownership block expires when a lane reaches the file,
    /// and a link block does not expire until the importer goes.
    /// </para>
    /// </summary>
    private static readonly string[] StayedNativeBlockedByAStockExtern =
    {
        "default_fov",
    };

    //-------------------------------------------------------------------------
    // THE INSTRUMENT, and it is reflection over ONE ASSEMBLY rather than the
    // registry. This is the second version; the first could not fail.
    //
    // ConVarRegistry.RegisterAll goes through LoadedAssemblies.All(anchor),
    // which sweeps the anchor's whole LOAD CONTEXT. In the GAME that is right
    // and is the point: each side has its own ALC and sees only its own
    // assemblies. In this TEST process there is one default context holding
    // Server and Client at once, so a registry built from either anchor holds
    // BOTH sides' declarations.
    //
    // Five of the knobs are declared on both sides, so a lookup that took the
    // first match found the OTHER side's copy — and a mutation of
    // violence_hblood's default from "1" to "0" in SourceSharp.Server left
    // every fact green. That is a check that cannot fail, and it is the reason
    // these read Assembly.GetTypes() directly: that is per-assembly by
    // construction, and every declaration of a name is checked rather than one
    // arbitrary one.
    //-------------------------------------------------------------------------

    /// <summary>Every [ConVar] declared BY THIS ASSEMBLY, by name.</summary>
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

    private static readonly Assembly ServerAssembly =
        typeof(SourceSharp.Server.AiShotStatsConVar).Assembly;

    private static readonly Assembly ClientAssembly =
        typeof(client::SourceSharp.Client.InterpNpcsConVar).Assembly;

    /// <summary>The declarations of one name on each side; empty when neither declares it.</summary>
    private static List<(string Side, ConVarAttribute Attr)> DeclarationsOf(string name)
    {
        var all = new List<(string, ConVarAttribute)>();

        if (DeclarationsIn(ServerAssembly).TryGetValue(name, out var s))
            all.Add(("SourceSharp.Server", s));
        if (DeclarationsIn(ClientAssembly).TryGetValue(name, out var c))
            all.Add(("SourceSharp.Client", c));

        return all;
    }

    /// <summary>
    /// Every declaration of one name, paired with the side that made it — one
    /// entry for a one-sided knob, two for a shared-file one. Asserting over
    /// ALL of them is what makes a one-sided edit visible.
    /// </summary>
    private static List<(string Side, ConVarAttribute Attr)> EveryDeclarationOf(string name)
    {
        var all = DeclarationsOf(name);

        Assert.True(all.Count > 0,
            name + " is declared by neither SourceSharp.Server nor SourceSharp.Client. "
                 + "Its C++ declaration is gone, so nothing declares it at all and the "
                 + "setting has silently ceased to exist.");

        return all;
    }

    private static Knob Expected(string name) => Moved.Single(k => k.Name == name);

    public static TheoryData<string> EveryMovedName()
    {
        var data = new TheoryData<string>();
        foreach (var knob in Moved)
            data.Add(knob.Name);

        return data;
    }

    //-------------------------------------------------------------------------
    // One behaviour per fact, per knob, per side that declares it.
    //-------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(EveryMovedName))]
    public void EveryMovedKnobIsDeclaredInManagedCode(string name)
    {
        foreach (var (side, attr) in EveryDeclarationOf(name))
            Assert.Equal(name, attr.Name);
    }

    [Theory]
    [MemberData(nameof(EveryMovedName))]
    public void EveryMovedKnobKeptItsCppDefault(string name)
    {
        foreach (var (side, attr) in EveryDeclarationOf(name))
            Assert.Equal(Expected(name).DefaultValue + " (" + side + ")",
                         attr.DefaultValue + " (" + side + ")");
    }

    [Theory]
    [MemberData(nameof(EveryMovedName))]
    public void EveryMovedKnobKeptItsCppFlags(string name)
    {
        foreach (var (side, attr) in EveryDeclarationOf(name))
            Assert.Equal(Expected(name).Flags + " (" + side + ")",
                         attr.Flags + " (" + side + ")");
    }

    [Theory]
    [MemberData(nameof(EveryMovedName))]
    public void EveryMovedKnobKeptItsCppHelpText(string name)
    {
        foreach (var (side, attr) in EveryDeclarationOf(name))
            Assert.Equal(Expected(name).Help + " (" + side + ")",
                         attr.Help + " (" + side + ")");
    }

    /// <summary>
    /// The registry really does pick these up and bind each to its declared
    /// default. The engine is absent, so registration is refused and the id is
    /// -1 — and a refused convar keeping what it declared is exactly what makes
    /// the facts above mean anything at runtime rather than only in metadata.
    /// </summary>
    [Fact]
    public void TheRegistryBindsEveryMovedKnobToItsDeclaredDefault()
    {
        var registered = BothSidesConVars.Registered(ServerAssembly, ClientAssembly);

        foreach (var knob in Moved)
        {
            var bound = registered.Where(
                c => string.Equals(c.Name, knob.Name, StringComparison.OrdinalIgnoreCase)).ToList();

            Assert.True(bound.Count > 0, knob.Name + " reached no registry at all.");

            foreach (var one in bound)
                Assert.Equal(knob.DefaultValue, one.DefaultValue);
        }
    }

    [Fact]
    public void NoSideDeclaresAConVarTheEngineDefines()
    {
        foreach (var name in EngineOwned)
            Assert.True(DeclarationsOf(name).Count == 0,
                name + " is the engine's; declaring it in C# would alias the engine's own");
    }

    [Fact]
    public void TheEngineOwnedListIsWhatTheDeclarationsOnceHeld()
    {
        // A list that lost its members would let the fact above pass on nothing.
        Assert.Equal(8, EngineOwned.Length);
    }

    [Fact]
    public void TheBlockedOneIsNativeOnTheClientOnly()
    {
        // plan_server_novalve.md P2.3b: default_fov's server half is C#'s and the
        // native declaration is the client's, for stock view.cpp's extern.
        Assert.True(DeclarationsIn(ServerAssembly).ContainsKey("default_fov"),
                    "default_fov's server half is not declared in SourceSharp.Server");

        string shared = File.ReadAllText(Path.Combine(RepoLayout.SdkTree, "src", "sourcesharp", "game",
                                                      "shared", "shell_shared.cpp"));
        Assert.Contains("#ifdef CLIENT_DLL\nConVar default_fov( \"default_fov\", \"90\", FCVAR_REPLICATED | FCVAR_CHEAT,",
                        shared.Replace("\r\n", "\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void AKnobThatOnlyOneSideNeedsIsNotDeclaredByBoth()
    {
        // A convar registered from two DLLs when only one reads it is a second
        // setting nobody meant, and Source says nothing about it. Every knob
        // that was declared on both sides turned out to be the engine's.
        foreach (var knob in Moved)
            Assert.Single(EveryDeclarationOf(knob.Name));
    }

    [Fact]
    public void TheOneStillBlockedByAStockExternIsNotDeclaredInManagedCode()
    {
        foreach (var name in StayedNativeBlockedByAStockExtern)
        {
            // THE CLIENT'S HALF ONLY, since plan_server_novalve.md P2.3b: the
            // stock extern is view.cpp's, which is a client TU, so the server's
            // half moved to C# and the native one is behind #ifdef CLIENT_DLL
            // (TheBlockedOneIsNativeOnTheClientOnly below).
            Assert.False(DeclarationsIn(ClientAssembly).ContainsKey(name),
                name + " is declared in client managed code, but its C++ declaration is still "
                     + "there — a stock TU holds an `extern ConVar` for it and the link "
                     + "needs the native object. Two declarations of one name is neither "
                     + "a compile error nor a link error, and nothing says which one the "
                     + "game read.");
        }
    }

    //-------------------------------------------------------------------------
    // THE TWO THAT STOPPED BEING BLOCKED (Phase 1b).
    //
    // One fact per half, rather than one fact for the pair, because the two
    // halves fail for different reasons and a single assertion would not say
    // which: a declaration that arrives in managed code WITHOUT the C++ one
    // being deleted leaves the game with two convars of one name, and a C++
    // declaration deleted without a managed one arriving loses the knob.
    //
    // The premise both rest on is PinnedFirst below: baseentity.cpp, whose
    // `extern ConVar` was the whole reason these two stayed native, is not
    // compiled into server_sourcesharp (src/server/native/CMakeLists.txt).
    //-------------------------------------------------------------------------

    /// <summary>
    /// The premise. Both knobs were blocked by an <c>extern ConVar</c> in
    /// <c>game/server/baseentity.cpp</c>; if that file ever comes back into the
    /// server project, the link needs native objects again and the two moves
    /// below have to be reconsidered rather than silently breaking the build.
    /// </summary>
    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void BaseentityCppIsNotInTheServerProject()
    {
        // server_sourcesharp's compiled sources (compile_commands.json, checked
        // against src/server/native/CMakeLists.txt); a missing or stale build throws.
        IReadOnlySet<string> server = CMakeGraph.CompiledSources(CMakeGraph.Server);

        // Proof of read: a shared file the server does compile.
        Assert.Contains("sourcesharp/game/shared/shell_movement.cpp", server);

        Assert.DoesNotContain(server, s => s.Contains("baseentity.cpp", StringComparison.Ordinal));
    }

    [Fact]
    public void AiLosModeIsDeclaredInTheServerAssembly()
    {
        Assert.True(DeclarationsIn(ServerAssembly).ContainsKey("ai_LOS_mode"),
            "ai_LOS_mode lost its declaration. Its C++ home, shell_ai_absent.cpp, was "
            + "deleted in Phase 1b, so managed code is the only place left that can "
            + "declare it.");
    }

    [Fact]
    public void AiLosModeKeptItsCppDefaultAndReplicatedFlag()
    {
        // Stock: ConVar ai_LOS_mode( "ai_LOS_mode", "0", FCVAR_REPLICATED );
        // -- no help text, and FCVAR_REPLICATED is part of the declaration even
        // though nothing on either side reads the value today.
        var declared = DeclarationsIn(ServerAssembly)["ai_LOS_mode"];

        Assert.Equal("0", declared.DefaultValue);
        Assert.Equal(ConVarFlags.Replicated, declared.Flags);
        Assert.Equal("", declared.Help ?? "");
    }

    [Fact]
    public void VehicleAutoaimScaleIsDeclaredInTheServerAssembly()
    {
        Assert.True(DeclarationsIn(ServerAssembly).ContainsKey("sv_vehicle_autoaim_scale"),
            "sv_vehicle_autoaim_scale lost its declaration. Its C++ home, "
            + "shell_subsystems_absent.cpp, was deleted in Phase 1b, so managed code is "
            + "the only place left that can declare it.");
    }

    [Fact]
    public void VehicleAutoaimScaleKeptItsCppDefault()
    {
        // Stock: ConVar sv_vehicle_autoaim_scale( "sv_vehicle_autoaim_scale", "8" );
        // -- no flags and no help text, and both of those are part of the value.
        var declared = DeclarationsIn(ServerAssembly)["sv_vehicle_autoaim_scale"];

        Assert.Equal("8", declared.DefaultValue);
        Assert.Equal(ConVarFlags.None, declared.Flags);
        Assert.Equal("", declared.Help ?? "");
    }

    //-------------------------------------------------------------------------
    // THE PERMISSION GATE (Phase 1b), and it gets more facts than the others
    // because getting it wrong is a security bug rather than a missing knob.
    //
    // sv_allow_point_servercommand is the only convar in the SDK built to
    // switch an entity off, and point_servercommand is the entity that can run
    // rcon_password or exec. Three things have to hold together: it is declared
    // exactly once, it is declared with stock's default and help, and the C++
    // declaration is gone -- because two convars of one name is neither a
    // compile error nor a link error, and nothing says which one the game read.
    //-------------------------------------------------------------------------

    [Fact]
    public void ThePointServerCommandGateIsDeclaredInTheServerAssembly()
    {
        Assert.True(DeclarationsIn(ServerAssembly).ContainsKey("sv_allow_point_servercommand"),
            "sv_allow_point_servercommand lost its declaration. PointServerCommand reads "
            + "it by name through IEngineConsole.FindConVarValue, and a name that is "
            + "registered nowhere reads as empty -- which Allows() must not treat as "
            + "permission.");
    }

    [Fact]
    public void ThePointServerCommandGateKeptItsCppDefaultAndHelp()
    {
        // Stock, in game/server/client.cpp by way of shell_client_orphans.cpp:
        //   ConVar sv_allow_point_servercommand( "sv_allow_point_servercommand",
        //       "always", FCVAR_NONE,
        //       "Allow use of point_servercommand entities in map. Potentially "
        //       "dangerous for untrusted maps.\n"
        //       "  disallow : Always disallow\n"
        //       "  always   : Allow for all maps" );
        var declared = DeclarationsIn(ServerAssembly)["sv_allow_point_servercommand"];

        Assert.Equal("always", declared.DefaultValue);
        Assert.Equal(ConVarFlags.None, declared.Flags);
        Assert.Equal(
            "Allow use of point_servercommand entities in map. Potentially dangerous for untrusted maps.\n"
            + "  disallow : Always disallow\n"
            + "  always   : Allow for all maps",
            declared.Help);
    }

    [Fact]
    public void ThePointServerCommandGateIsDeclaredOnTheServerOnly()
    {
        // A second registration from the client DLL would be a second setting
        // for the same permission, and Source says nothing about it.
        Assert.False(DeclarationsIn(ClientAssembly).ContainsKey("sv_allow_point_servercommand"),
            "the gate is a server permission; a client-side declaration is a second "
            + "setting nobody meant.");
    }

    //-------------------------------------------------------------------------
    // THE HOLE THE FOUR r_rain* KNOBS LIVED IN FOR A WHOLE PHASE.
    //
    // EveryMovedKnobIsGoneFromTheShimsCpp looks for `ConVar<ws>name(`, which is
    // every FILE-SCOPE declaration and no by-name ref. It is not every
    // declaration. A ConVar that is a static MEMBER is defined as
    //
    //     ConVar CClient_Precipitation::s_rainwidth( "r_rainwidth", "0.5", ... );
    //
    // -- the C++ identifier is `s_rainwidth` and the convar's NAME appears only
    // as the first string argument. Four such definitions sat in
    // shell_effects_sim.cpp while that file's own header said all thirty-two of
    // its knobs had moved to managed code, and the fact above was green the
    // whole time, because it was asking about a spelling those four do not use.
    //
    // So this asks about the NAME rather than the identifier: any `ConVar`
    // constructor in the shim whose first argument is a moved knob's name. The
    // word boundary is what keeps `CManagedConVarRef` and a plain `ConVarRef`
    // out -- in both, the letters "ConVar" are glued to a word character on one
    // side -- and ARefAndAMemberAreToldApart below is the control on that.
    //
    // IT DOES NOT CROSS A LINE, and that is not tidiness either. The first
    // version of this needle did, and it matched the PROSE above three of the
    // refs: a comment containing the word "ConVar" on one line and the ref's own
    // `name( "name" )` two lines below it read as one declaration. Three facts
    // went red against files that had nothing wrong with them.
    // AProseMentionIsNotADeclaration is the control on the fix.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A C++ <c>ConVar</c> constructor whose first argument is this name,
    /// however the object itself is spelled.
    /// </summary>
    private static Regex DeclarationOf(string name) => new(
        DeclarationNeedle + "\"" + Regex.Escape(name) + "\"",
        RegexOptions.Compiled);

    [Theory]
    [MemberData(nameof(EveryMovedName))]
    public void NoNativeConVarIsConstructedWithAMovedKnobsName(string name)
    {
        var shim = Path.Combine(RepoLayout.SdkTree, "src", "sourcesharp");
        var pattern = DeclarationOf(name);

        var offenders = new List<string>();

        foreach (var path in Directory.GetFiles(shim, "*.cpp", SearchOption.AllDirectories))
        {
            if (pattern.IsMatch(File.ReadAllText(path)))
                offenders.Add(Path.GetFileName(path));
        }

        Assert.True(offenders.Count == 0,
            name + " is declared in managed code AND constructed as a native ConVar in "
                 + string.Join(", ", offenders)
                 + ". Two convars of one name is neither a compile error nor a link "
                 + "error: the engine keeps one, drops the other with a single console "
                 + "line, and the loser answers its compiled-in default forever.");
    }

    //-------------------------------------------------------------------------
    // THE DEFECT THE NEEDLE ABOVE WAS BUILT TO FIND, ASKED AS A RULE RATHER
    // THAN AS A LIST.
    //
    // mat_reduceparticles was declared as a native `static ConVar` in BOTH
    // shell_smokestack_sim.cpp AND shell_smokevolume_sim.cpp -- two
    // registrations of one name, in one DLL. That is neither a compile error
    // nor a link error: the engine keeps the first and drops the second with
    // one console line, and the loser's `.GetBool()` answered its compiled-in
    // default for the life of the process. Whichever half of the smoke code
    // lost, a player who set the knob changed only the other half.
    //
    // The theory above is a list: it catches a duplicate only for a name
    // somebody has already moved. This asks the question of every name the
    // shim declares, so the NEXT one is caught before it ships rather than
    // when a lane happens to read the two files together.
    //
    // It reads the CONSTRUCTOR ARGUMENT, not the C++ identifier, for the same
    // reason the theory above does -- the two smoke declarations had identical
    // identifiers and the four r_rain* had identifiers that were not their
    // names at all.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The needle, shared by the theory above and the duplicate scan below:
    /// <c>ConVar &lt;identifier&gt;( "name"</c> on ONE line.
    ///
    /// <para>
    /// The identifier is required, and it may carry a <c>::</c> — which is what
    /// makes <c>ConVar CClient_Precipitation::s_rainwidth( "r_rainwidth", … )</c>
    /// a match. The word boundary on <c>ConVar</c> keeps
    /// <c>CManagedConVarRef</c> and a plain <c>ConVarRef</c> out: in both, the
    /// letters are glued to a word character on one side.
    /// </para>
    ///
    /// <para>
    /// It does not cross a line. The first version did and matched PROSE — a
    /// comment naming the type joined to a ref's own <c>name( "name" )</c> two
    /// lines below it — which reddened three files that had nothing wrong with
    /// them. <see cref="AProseMentionIsNotADeclaration"/> is the control.
    /// </para>
    /// </summary>
    private const string DeclarationNeedle =
        "\\bConVar\\s+[A-Za-z_][A-Za-z0-9_:]*\\s*\\(\\s*";

    /// <summary>
    /// The text with whole-line <c>//</c> comments dropped.
    ///
    /// <para>
    /// Prose in this tree QUOTES declarations, and quoting one is not making
    /// one: <c>shell_gamerules.cpp:111</c> explains itself with the line
    /// "<c>// ConVar skill( "skill", "1" ), and on a listen server …</c>" one
    /// screen above the declaration it is about, and the scan read the pair as
    /// two registrations of <c>skill</c>. The gtests that scrape this tree
    /// strip comments for the same reason, and their own note gives it: a check
    /// a comment can satisfy is a check about comments.
    /// </para>
    /// </summary>
    private static string WithoutWholeLineComments(string text)
    {
        var kept = new List<string>();

        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.TrimStart();

            if (!trimmed.StartsWith("//", StringComparison.Ordinal))
                kept.Add(line);
        }

        return string.Join("\n", kept);
    }

    [Fact]
    public void ANativeMemberConVarIsCaughtByTheNameNeedle()
    {
        // The positive control, and it is the exact line that was missed: a
        // static member definition, whose C++ identifier is not the name.
        Assert.Matches(DeclarationOf("r_rainwidth"),
            "ConVar CClient_Precipitation::s_rainwidth( \"r_rainwidth\", \"0.5\", FCVAR_CHEAT );");
    }

    [Fact]
    public void ARefAndAMemberAreToldApart()
    {
        // The negative control. A by-name read names the same string and must
        // NOT be read as a declaration, or the fact above could never pass and
        // would be strictness rather than a check.
        Assert.DoesNotMatch(DeclarationOf("r_rainwidth"),
            "static CManagedConVarRef r_rainwidth( \"r_rainwidth\" );");

        // Nor may a plain engine-side ConVarRef, which is how code outside this
        // shim reaches a name somebody else declared.
        Assert.DoesNotMatch(DeclarationOf("mat_reduceparticles"),
            "static ConVarRef mat_reduceparticles( \"mat_reduceparticles\" );");
    }

    [Fact]
    public void AProseMentionIsNotADeclaration()
    {
        // The other negative control, and the one that was learned the hard
        // way. A comment that names the type and a ref that names the convar
        // are two different lines, and a needle that spans lines joins them
        // into a declaration that is not there.
        Assert.DoesNotMatch(DeclarationOf("cl_sporeclipdistance"),
            "// the rule is plan_entity.md \u00a76's: no ConVar's default or semantics\n"
            + "// is declared natively. The read below is unchanged.\n"
            + "static CManagedConVarRef cl_sporeclipdistance( \"cl_sporeclipdistance\" );");
    }

    [Fact]
    public void ARefIsNotMistakenForADeclaration()
    {
        // The needle above is a substring, and a substring needle that matched
        // the REF as well would make EveryMovedKnobIsGoneFromTheShimsCpp
        // impossible to pass rather than merely strict -- so the two spellings
        // are held apart explicitly. Without this, tightening the needle later
        // could silently turn that fact vacuous.
        const string declaration = "ConVar violence_hblood(";
        const string reference   = "CManagedConVarRef violence_hblood(";

        Assert.False(reference.Contains(declaration),
            "a by-name ref must not contain the declaration needle, or the "
            + "gone-from-the-cpp fact cannot tell a read from a declaration.");
        Assert.True(("static " + declaration).Contains(declaration),
            "the declaration needle must still match a real declaration.");
    }
}
