using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Guards the scanning that <see cref="UserMessageHookParityTests"/> leans on.
///
/// <para>
/// The parity check is only as good as the two scans under it, and both can
/// fail quietly in the direction that PASSES: a hook regex that matches nothing
/// makes the hooked set empty and every comparison vacuous. That does not show
/// up as an error. (The other scan, of the compiled file list, is
/// <see cref="CMakeGraph.CompiledSources"/> now, which throws on a missing,
/// empty or stale build; the vpc <c>$File</c> scanner and the facts that drove
/// it went with VPC.) So the
/// scanners are driven here against literal snippets, where the expected answer
/// is written next to the input, in BOTH directions — something that must be
/// found, and something that must not.
/// </para>
/// </summary>
public class UserMessageHookScanTests
{
    // ---------------------------------------------------------------
    // Hook scanning.
    // ---------------------------------------------------------------

    [Fact]
    public void FindsAPlainHookMacro()
    {
        var names = UserMessageHookParityTests.HookedNames("\tHOOK_MESSAGE( VGUIMenu );\n");

        Assert.Contains("VGUIMenu", names);
    }

    [Fact]
    public void FindsTheMessageNameOfAHudHookMacroNotTheClassName()
    {
        // HOOK_HUD_MESSAGE( CHudGeiger, Geiger ) hooks "Geiger". Taking the
        // first argument instead would collect class names and compare them
        // against message names, which fails for the wrong reason.
        var names = UserMessageHookParityTests.HookedNames(
            "\tHOOK_HUD_MESSAGE( CHudGeiger, Geiger );\n");

        Assert.Contains("Geiger", names);
        Assert.DoesNotContain("CHudGeiger", names);
    }

    [Fact]
    public void FindsAStringLiteralHook()
    {
        var names = UserMessageHookParityTests.HookedNames(
            "\tusermessages->HookMessage( \"AchievementEvent\", MsgFunc_AchievementEvent );\n");

        Assert.Contains("AchievementEvent", names);
    }

    [Fact]
    public void FindsAUserMessageRegisterMacro()
    {
        var names = UserMessageHookParityTests.HookedNames("USER_MESSAGE_REGISTER( SavedConvar );\n");

        Assert.Contains("SavedConvar", names);
    }

    [Fact]
    public void FindsAStaticConstructorUserMessageMacro()
    {
        var names = UserMessageHookParityTests.HookedNames("USER_MESSAGE( BreakModel )\n{\n}\n");

        Assert.Contains("BreakModel", names);
    }

    [Fact]
    public void DoesNotTreatAHookThroughAVariableAsALiteralName()
    {
        // The SourceSharp shell's own hook. It registers the same name on the
        // line above, so it is not a name this scan can resolve — and inventing
        // one ("pName") would be a permanent false failure.
        var names = UserMessageHookParityTests.HookedNames(
            "\tusermessages->HookMessage( pName, SourceSharp_GetHookTable()[ nIndex ] );\n");

        Assert.DoesNotContain("pName", names);
        Assert.Empty(names);
    }

    [Fact]
    public void IgnoresAHookThatIsCommentedOut()
    {
        var names = UserMessageHookParityTests.HookedNames(
            "\tHOOK_MESSAGE( Live );\n\t// HOOK_MESSAGE( Commented );\n");

        Assert.Contains("Live", names);
        Assert.DoesNotContain("Commented", names);
    }

    [Fact]
    public void IgnoresAHookInsideABlockComment()
    {
        var names = UserMessageHookParityTests.HookedNames(
            "HOOK_MESSAGE( Live );\n/*\n HOOK_MESSAGE( OffOne );\n HOOK_MESSAGE( OffTwo );\n*/\n");

        Assert.Contains("Live", names);
        Assert.DoesNotContain("OffOne", names);
        Assert.DoesNotContain("OffTwo", names);
    }

    // ---------------------------------------------------------------
    // Native registration scanning.
    // ---------------------------------------------------------------

    [Fact]
    public void FindsANativeRegistration()
    {
        var names = UserMessageHookParityTests.NativelyRegisteredNames(
            "\tusermessages->Register( \"SPHapWeapEvent\", 4 );\n");

        Assert.Contains("SPHapWeapEvent", names);
    }

    [Fact]
    public void IgnoresACommentedNativeRegistration()
    {
        var names = UserMessageHookParityTests.NativelyRegisteredNames(
            "\tusermessages->Register( \"Live\", 1 );\n\t// usermessages->Register( \"Off\", 1 );\n");

        Assert.Contains("Live", names);
        Assert.DoesNotContain("Off", names);
    }

    // ---------------------------------------------------------------
    // The comment stripper both scans share.
    // ---------------------------------------------------------------

    [Fact]
    public void StripperLeavesCodeAloneAndRemovesComments()
    {
        string stripped = UserMessageHookParityTests.StripComments(
            "int a; // gone\nint b; /* also gone */ int c;\n");

        Assert.Contains("int a;", stripped);
        Assert.Contains("int b;", stripped);
        Assert.Contains("int c;", stripped);
        Assert.DoesNotContain("gone", stripped);
    }

    [Fact]
    public void StripperKeepsCommentMarkersInsideStringLiterals()
    {
        // A registration whose NAME contains a slash pair must survive. Without
        // the string-literal state this would eat the rest of the file and make
        // every later hook invisible — the vacuous-pass direction.
        string stripped = UserMessageHookParityTests.StripComments(
            "Register( \"a/*b\", 1 ); Register( \"Later\", 2 );\n");

        Assert.Contains("Later", stripped);
    }
}
