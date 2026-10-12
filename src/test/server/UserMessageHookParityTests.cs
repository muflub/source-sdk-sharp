using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// That the client build hooks and registers no user message natively, and
/// that C# declares the messages the stock client hooked.
///
/// <para>
/// This began as the invariant behind <c>usermessages.cpp:132</c>: stock's
/// HookMessage asserted on a name nothing registered, and a hooked name C# did
/// not declare cost an assert per hook at load. That route is gone (lane HC,
/// 2026-10-04): the six stock HOOK_MESSAGE sites are <c>#ifndef SOURCESHARP</c>
/// in the fork, their effects are C#'s handlers', and no native HookMessage is
/// left to assert (usermessage_forwarders_test's NoNativeHookRouteRemains). What
/// stays worth pinning is that no native hook or registration comes back, read
/// from the files the build compiles, and that the stock messages are still
/// C#'s declarations.
/// </para>
/// </summary>
public class UserMessageHookParityTests
{
    // ------------------------------------------------------------------
    // Scanners. Pure and public so UserMessageHookScanTests can drive them
    // with literal snippets, where the right answer sits next to the input.
    // ------------------------------------------------------------------

    /// <summary>
    /// Strips // and /* */ comments, so a hook someone commented out does not
    /// read as live. Preserves newlines so nothing else shifts.
    /// </summary>
    internal static string StripComments(string code)
    {
        var sb = new StringBuilder(code.Length);
        bool inLine = false, inBlock = false, inString = false;

        for (int i = 0; i < code.Length; i++)
        {
            char c = code[i];
            char n = i + 1 < code.Length ? code[i + 1] : '\0';

            if (inLine)
            {
                if (c == '\n') { inLine = false; sb.Append(c); }
                continue;
            }

            if (inBlock)
            {
                if (c == '*' && n == '/') { inBlock = false; i++; }
                else if (c == '\n') sb.Append(c);
                continue;
            }

            if (inString)
            {
                if (c == '\\' && n != '\0') { sb.Append(c).Append(n); i++; continue; }
                if (c == '"') inString = false;
                sb.Append(c);
                continue;
            }

            if (c == '/' && n == '/') { inLine = true; continue; }
            if (c == '/' && n == '*') { inBlock = true; i++; continue; }
            if (c == '"') { inString = true; sb.Append(c); continue; }

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Drops the arms of <c>#ifndef SOURCESHARP</c> / <c>#if !defined( SOURCESHARP )</c>
    /// and the <c>#else</c> arm of <c>#ifdef SOURCESHARP</c> / <c>#if defined( SOURCESHARP )</c>:
    /// what this build does not compile. Every other conditional keeps both
    /// arms, as before. Preserves newlines.
    /// </summary>
    internal static string StripNonSourceSharpArms(string code)
    {
        var sb = new StringBuilder(code.Length);

        // Per open conditional: whether it tests SOURCESHARP, whether its
        // current arm is dropped, and the drop state outside it.
        var stack = new Stack<(bool OnSourceSharp, bool Drop, bool Outer)>();
        bool drop = false;

        foreach (string line in code.Split('\n'))
        {
            string t = line.TrimStart();
            string directive = t.StartsWith('#') ? Regex.Replace(t[1..].TrimStart(), @"\s+", " ") : string.Empty;

            if (directive.StartsWith("if", StringComparison.Ordinal))
            {
                bool not = Regex.IsMatch(directive, @"^ifndef SOURCESHARP\b|^if !\s*defined\s*\(?\s*SOURCESHARP\s*\)?\s*$");
                bool yes = Regex.IsMatch(directive, @"^ifdef SOURCESHARP\b|^if defined\s*\(?\s*SOURCESHARP\s*\)?\s*$");
                stack.Push((not || yes, drop, drop));
                if (not) drop = true;
                sb.Append('\n');
                continue;
            }

            if (directive.StartsWith("else", StringComparison.Ordinal) || directive.StartsWith("elif", StringComparison.Ordinal))
            {
                if (stack.Count > 0 && stack.Peek().OnSourceSharp)
                {
                    var top = stack.Peek();
                    drop = top.Outer || !drop;
                }
                sb.Append('\n');
                continue;
            }

            if (directive.StartsWith("endif", StringComparison.Ordinal))
            {
                if (stack.Count > 0)
                    drop = stack.Pop().Outer;
                sb.Append('\n');
                continue;
            }

            if (!drop)
                sb.Append(line);
            sb.Append('\n');
        }

        return sb.ToString();
    }

    // HOOK_MESSAGE( Name ) and HOOK_HUD_MESSAGE( Class, Name ).
    private static readonly Regex HookMacro =
        new(@"\bHOOK_MESSAGE\s*\(\s*([A-Za-z_]\w*)\s*\)", RegexOptions.Compiled);

    private static readonly Regex HookHudMacro =
        new(@"\bHOOK_HUD_MESSAGE\s*\(\s*[A-Za-z_]\w*\s*,\s*([A-Za-z_]\w*)\s*\)", RegexOptions.Compiled);

    // usermessages->HookMessage( "Name", fn ). A literal only: the SourceSharp
    // shell hooks through a variable, and that one registers the same name on
    // the line above, so it is not a hook this check can or should resolve.
    private static readonly Regex HookLiteral =
        new(@"HookMessage\s*\(\s*""([^""]+)""", RegexOptions.Compiled);

    // USER_MESSAGE_REGISTER( Name ) hooks via CUserMessageRegister::RegisterAll,
    // and USER_MESSAGE( Name ) hooks from a static constructor.
    private static readonly Regex UserMessageRegisterMacro =
        new(@"\bUSER_MESSAGE_REGISTER\s*\(\s*([A-Za-z_]\w*)\s*\)", RegexOptions.Compiled);

    private static readonly Regex UserMessageMacro =
        new(@"\bUSER_MESSAGE\s*\(\s*([A-Za-z_]\w*)\s*\)", RegexOptions.Compiled);

    // usermessages->Register( "Name", size )
    private static readonly Regex RegisterLiteral =
        new(@"\bRegister\s*\(\s*""([^""]+)""\s*,", RegexOptions.Compiled);

    /// <summary>Every message name this C++ text HOOKS.</summary>
    internal static SortedSet<string> HookedNames(string code)
    {
        string src = StripNonSourceSharpArms(StripComments(code));
        var found = new SortedSet<string>(StringComparer.Ordinal);

        foreach (Regex r in new[] { HookMacro, HookHudMacro, HookLiteral, UserMessageRegisterMacro })
            foreach (Match m in r.Matches(src))
                found.Add(m.Groups[1].Value);

        // USER_MESSAGE( X ) but not the USER_MESSAGE_REGISTER( X ) that shares
        // its prefix — that one is already counted above.
        foreach (Match m in UserMessageMacro.Matches(src))
            found.Add(m.Groups[1].Value);

        return found;
    }

    /// <summary>Every message name this C++ text REGISTERS natively, anywhere.</summary>
    internal static SortedSet<string> NativelyRegisteredNames(string code)
    {
        string src = StripComments(code);
        var found = new SortedSet<string>(StringComparer.Ordinal);

        foreach (Match m in RegisterLiteral.Matches(src))
            found.Add(m.Groups[1].Value);

        return found;
    }

    /// <summary>
    /// The body of a function definition, brace-matched; null when this text
    /// does not define it.
    /// </summary>
    internal static string? FunctionBody(string code, string functionName)
    {
        string src = StripComments(code);
        var signature = new Regex(@"\b" + Regex.Escape(functionName) + @"\s*\([^;{)]*\)\s*\{");

        Match m = signature.Match(src);
        if (!m.Success)
            return null;

        int open = src.IndexOf('{', m.Index);
        if (open < 0)
            return null;

        int depth = 0;
        for (int i = open; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0)
                return src.Substring(open + 1, i - open - 1);
        }

        return null;
    }

    /// <summary>Every function name called from a body.</summary>
    internal static SortedSet<string> CalledFunctions(string body)
    {
        var call = new Regex(@"\b([A-Za-z_]\w*)\s*\(");
        var found = new SortedSet<string>(StringComparer.Ordinal);

        foreach (Match m in call.Matches(StripComments(body)))
            found.Add(m.Groups[1].Value);

        return found;
    }

    /// <summary>
    /// Message names registered natively from INSIDE one of the named
    /// functions.
    ///
    /// <para>
    /// Scoped rather than file-wide, and that is the whole point.
    /// <c>usermessages.cpp</c> defines <c>RegisterScriptMessages()</c>, which
    /// registers "SavedConvar" — but nothing in this mod calls it: the shell's
    /// <c>RegisterUserMessages()</c> calls only <c>RegisterHapticMessages()</c>,
    /// and SavedConvar is a C# <c>[UserMessage]</c> here. Counting that dead
    /// definition made the parity check pass while C# no longer declared the
    /// name, which is precisely the failure the check exists to catch.
    /// </para>
    /// </summary>
    internal static SortedSet<string> RegisteredNamesIn(string code, IEnumerable<string> functionNames)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);

        foreach (string fn in functionNames)
        {
            string? body = FunctionBody(code, fn);
            if (body is null)
                continue;

            foreach (Match m in RegisterLiteral.Matches(body))
                found.Add(m.Groups[1].Value);
        }

        return found;
    }

    // THE FILE LIST THIS SUITE SCANS IS THE ONE THE BUILD COMPILES, by
    // construction: CMakeGraph.CompiledSources(Client) is compile_commands.json,
    // which it requires to equal src/client/native/CMakeLists.txt's list exactly
    // and throws on otherwise. That cross-check used to be a fact here
    // (TheFileListThisSuiteScansIsTheOneTheBuildCompiles), comparing a $File
    // scanner's reading of client_csharp.vpc / server_csharp.vpc with VPC's ninja
    // graph -- the scanner missed 18 client and 5 server files over line
    // continuations before plan_cworld_trace.md T5. VPC, its scanner and its graph
    // are gone; the helper's own check replaces the fact, and CMakeGraphTests
    // holds it.

    // ------------------------------------------------------------------
    // The parity check itself.
    // ------------------------------------------------------------------

    [Fact]
    public void TheClientBuildHooksAndRegistersNoUserMessageNatively()
    {
        // client_sourcesharp's compiled sources, farm-relative
        // (compile_commands.json, equal to src/client/native/CMakeLists.txt's
        // list; CMakeGraph throws on a missing, empty or stale build). Reading
        // anything wider -- client_csharp.vpc once $Included client_base.vpc --
        // would count hooks in files the binary does not contain.
        IReadOnlySet<string> built = CMakeGraph.CompiledSources(CMakeGraph.Client);

        // The native registration scan is no longer SCOPED to a live registrar,
        // because there is no longer a live registrar to scope it to.
        //
        // It used to follow shell_shared.cpp's RegisterUserMessages() and count
        // only what that reached, so that usermessages.cpp's dead
        // RegisterScriptMessages() would not be credited. Phase 5a step 3 took
        // both of those files' registrations out of the build: C# is the only
        // registrar, and shell_usermessage_registry.cpp's CUserMessages::Register
        // stores whatever name it is HANDED, never a literal of its own.
        //
        // So the scan is now file-wide and the expectation is zero. That is
        // strictly stronger: a registration added anywhere in a compiled client
        // file, live or not, is a name landing at an index C# does not know
        // about, and it fails here rather than being reasoned about.

        var hooked = new SortedSet<string>(StringComparer.Ordinal);
        var nativelyRegistered = new SortedSet<string>(StringComparer.Ordinal);
        var hookSource = new Dictionary<string, string>(StringComparer.Ordinal);
        int read = 0;

        foreach (string rel in built)
        {
            string path = RepoLayout.Sdk("src", rel);
            Assert.True(File.Exists(path), $"client_sourcesharp compiles {rel}, which is not at {path}");

            read++;
            string code = File.ReadAllText(path);

            foreach (string name in HookedNames(code))
            {
                hooked.Add(name);
                if (!hookSource.ContainsKey(name))
                    hookSource[name] = rel;
            }

            foreach (string name in NativelyRegisteredNames(code))
                nativelyRegistered.Add(name);
        }

        // Every compiled file was scanned, not a handful.
        Assert.Equal(built.Count, read);
        // NO NATIVE HOOK IS LEFT (the user-message payload model, 2026-10-04):
        // the six stock HOOK_MESSAGE sites are `#ifndef SOURCESHARP` in the
        // fork and their handlers are C#'s. The control is the scanner over
        // planted text, so an empty set is the build's and not a blind scan.
        Assert.Equal(new[] { "Planted" },
                     HookedNames("HOOK_MESSAGE( Planted );\n#ifndef SOURCESHARP\nHOOK_MESSAGE( Gone );\n#endif\n").ToArray());
        Assert.True(hooked.Count == 0,
            "the client hooks user messages natively again; every message's handler is C#'s: "
            + string.Join(", ", hooked.Select(h => $"{h} ({hookSource[h]})")));

        // What native registration is LEFT in the client build: NONE, and that
        // is new. plan_cworld_trace.md T5.
        //
        // It used to be six. haptic_msgs.cpp was compiled — the client hooked
        // all six haptics from HookHapticMessages() in it — and it DEFINES
        // RegisterHapticMessages with six literal Register calls, which Phase 5a
        // step 2 left callerless. T5 took the file off client_csharp.vpc (it
        // carried haptic_utils.cpp's [$WINDOWS&&!$X360] tag there), because on Linux
        // every one of its six handlers is an empty `#ifdef WIN32` body. The
        // last literal Register in the client's C++ went with it.
        //
        // ZERO is the strongest form of the property this assertion always had:
        // a native Register lands a name at an index C# does not know about and
        // shifts every managed message relative to the other side's, silently,
        // on both. Any registration coming back fails here.
        Assert.True(nativelyRegistered.Count == 0,
            "no native user message registration may remain in the client build — C# owns the "
            + "whole list and a native Register would shift every index. Found: "
            + string.Join(", ", nativelyRegistered));

        // And the file those six came from is genuinely out of the build rather
        // than merely unscanned, which would empty the set above just as well.
        Assert.DoesNotContain(built,
            f => f.EndsWith("haptics/haptic_msgs.cpp", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The names C# declares are what the wire indices are built from, so the
    /// stock client's hooked messages must not silently drop out of the set.
    /// </summary>
    [Fact]
    public void TheCSharpArmStillDeclaresTheMessagesTheClientNeeds()
    {
        var declared = new SortedSet<string>(
            typeof(SourceSharp.Shared.FadeMessage).Assembly.GetTypes()
                .Select(t => t.GetCustomAttribute<UserMessageAttribute>())
                .Where(a => a is not null)
                .Select(a => a!.Name),
            StringComparer.Ordinal);

        // The eight the stock client hooked, from its __MsgFunc_* symbols plus
        // achievementmgr.cpp's literal hook. Listed here deliberately: this is
        // the set whose loss once produced the reported assert, and each one's
        // effect is a C# handler now.
        foreach (string required in new[]
                 {
                     "VGUIMenu", "Rumble", "Shake", "Fade",
                     "VoiceMask", "RequestState", "SavedConvar", "AchievementEvent",
                 })
        {
            Assert.True(declared.Contains(required),
                $"C# must declare [UserMessage(\"{required}\")]: the stock client hooked it, "
                + "and its handler is C#'s");
        }
    }
}
