using System.Reflection;
using System.Text.RegularExpressions;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Which messages native code still reads or writes, DERIVED from the build
/// rather than kept by hand, held against the messages that have a managed
/// payload.
///
/// <para>
/// A message a native reader or a native sender touches has its layout written
/// down twice: once in the managed payload's declaration, once in a
/// <c>READ_*</c>/<c>WRITE_*</c> sequence in C++. The payload model's one wire
/// convention matches Valve's byte-aligned encoding member for member, so the
/// two can agree, but nothing makes them: a member added to the payload is a
/// misparse in the native hook. The scan reads hooks written through the macro
/// AND as a literal call (<c>achievementmgr.cpp</c> does that), and native
/// SENDERS, which fix a layout just as hard.
/// </para>
/// </summary>
public class UserMessageEncodingGroupTests
{
    /// <summary>Every message name a compiled .cpp HOOKS or SENDS.</summary>
    private static SortedSet<string> NativelyOwned()
    {
        // Each DLL's own compiled sources and nothing else: server_sourcesharp
        // and client_sourcesharp from compile_commands.json, each equal to its
        // CMakeLists list (CMakeGraph throws on a missing, empty or stale build).
        // Reading anything wider would union files the build does not compile
        // into the "natively owned" set.
        IReadOnlySet<string> built = CMakeGraph.GameSources();

        var owned = new SortedSet<string>(StringComparer.Ordinal);
        int read = 0;

        foreach (string rel in built)
        {
            string path = RepoLayout.Sdk("src", rel);
            Assert.True(File.Exists(path), $"the build compiles {rel}, which is not at {path}");

            read++;
            string code = UserMessageHookParityTests.StripNonSourceSharpArms(
                UserMessageHookParityTests.StripComments(File.ReadAllText(path)));

            foreach (string name in UserMessageHookParityTests.HookedNames(code))
                owned.Add(name);

            foreach (Match m in NativeSend.Matches(code))
                owned.Add(m.Groups[1].Value);
        }

        // Every compiled file was scanned: an empty or partial scan would let
        // every message look free.
        Assert.Equal(built.Count, read);

        return owned;
    }

    // UserMessageBegin( filter, "Name" ). The literal only: a send through a
    // variable is SourceSharp's own generic path, which carries whatever bytes
    // managed code handed it and fixes no layout.
    private static readonly Regex NativeSend =
        new(@"UserMessageBegin\s*\([^;)]*?,\s*""([^""]+)""\s*\)", RegexOptions.Compiled);

    /// <summary>Every message class that names a payload (IUserMessage&lt;T&gt;).</summary>
    private static List<Type> Packed()
        => typeof(SourceSharp.Shared.FadeMessage).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<UserMessageAttribute>() is not null)
            .Where(t => t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IUserMessage<>)))
            .ToList();

    /// <summary>
    /// Payload messages native code still reads or writes while its half is
    /// ported (plan: all user messages in managed code). Only ever shrinks;
    /// empty when the port is done.
    /// </summary>
    private static readonly string[] StillNativeToo = [];

    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void NoMessageWithAGeneratedPackerHasANativeReaderOrSender()
    {
        var owned = NativelyOwned();

        // EVERY MESSAGE IS C#'s (the user-message payload model): no compiled
        // .cpp hooks or sends one any more. The control is the scanner itself,
        // over planted text: a live hook and send are found, and the same
        // hook in a `#ifndef SOURCESHARP` arm is not, so an empty set here is
        // the build's answer and not a scanner that stopped reading. (Fade and
        // CloseCaption were the anchors while native code still owned them.)
        string planted = "HOOK_MESSAGE( Live );\nUserMessageBegin( filter, \"Sent\" );\n"
                       + "#ifndef SOURCESHARP\nHOOK_MESSAGE( Dead );\n#else\nHOOK_MESSAGE( Kept );\n#endif\n";
        string scanned = UserMessageHookParityTests.StripNonSourceSharpArms(UserMessageHookParityTests.StripComments(planted));
        var plantedHooks = UserMessageHookParityTests.HookedNames(planted);
        Assert.Equal(new[] { "Kept", "Live" }, plantedHooks.ToArray());
        Assert.Equal("Sent", NativeSend.Match(scanned).Groups[1].Value);

        Assert.DoesNotContain(UserMessageKey.Of<SourceSharp.Shared.LayoutCheckMessage>(), owned);
        Assert.DoesNotContain("SourceSharpLayout", owned);
        Assert.True(owned.Count == 0,
            "native code reads or writes these messages again; every message is a C# payload: "
            + string.Join(", ", owned));

        var wrong = new List<string>();

        foreach (Type type in Packed())
        {
            string name = type.GetCustomAttribute<UserMessageAttribute>()!.Name;
            if (owned.Contains(name) && !StillNativeToo.Contains(name))
                wrong.Add(name);
        }

        Assert.True(wrong.Count == 0,
            "a message with a managed payload is read or written by native code too, so its "
            + "layout is stated twice. Port the native half: " + string.Join(", ", wrong));
    }

    [Fact]
    public void AtLeastOneMessageDoesHaveAGeneratedPacker()
    {
        // The other half. A tree with no packers at all satisfies the rule
        // above trivially, and would go on satisfying it if the generator were
        // deleted.
        Assert.True(Packed().Count > 0,
            "no message has a generated packer, so the rule above is vacuous");
    }

    [Fact]
    public void TheLayoutHandshakeKeepsItsFixedSize()
    {
        // The trap plan_gamedll.md Part 10c names by name, held separately so a
        // failure says which one. It is group A by the plan's own grep -- no
        // HOOK_MESSAGE anywhere.
        //
        // THE REASON CHANGED AT plan_shim.md §5 3e AND THE RULE DID NOT. It
        // used to be "a hand-written native sender writes three WRITE_LONGs and
        // knows nothing about a schema hash". That sender is gone; the payload
        // is ServerModule.SendLayoutCheck's three WriteInts. What has not
        // changed is that this message is FIXED SIZE: the engine validates a
        // fixed-size user message against the size it was registered with and
        // DROPS one that does not match, silently. A generated packer prepends
        // two schema-hash bytes, which makes every send fourteen bytes against
        // a twelve-byte registration -- a handshake that is never delivered and
        // never reported.
        Type layout = typeof(SourceSharp.Shared.LayoutCheckMessage);

        // 24. The third long is the engine-block fingerprint (plan_entity.md
        // 3.2); the last three are the protocol-list hashes
        // (plan_side_isolation §2.1). Size is a wire contract the engine
        // validates against, so this number, the writer's six WriteInts and
        // the reader's six ReadInts are one fact in three places --
        // LayoutHandshakeTests holds the other two against it.
        Assert.True(layout.GetCustomAttribute<UserMessageAttribute>()!.Size == 24,
            "and it keeps its fixed size, which is what the managed writer writes");
    }

    [Fact]
    public void AFixedSizeIsWhatItsPayloadWrites()
    {
        // Size in the declaration is a wire contract: the engine validates a
        // fixed-size message against it and drops one that differs. The
        // generator refuses a mismatch (SSMSG009); this holds what the BUILD
        // generated to it, so a generator that stopped checking shows here.
        foreach (Type type in Packed())
        {
            var attr = type.GetCustomAttribute<UserMessageAttribute>()!;
            if (attr.Size < 0)
                continue;

            Type payload = type.GetInterfaces()
                .Single(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IUserMessage<>))
                .GetGenericArguments()[0];

            int written = (int)typeof(UserMessageEncodingGroupTests)
                .GetMethod(nameof(DefaultLength), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(payload).Invoke(null, null)!;

            Assert.True(written == attr.Size,
                $"{attr.Name} declares Size = {attr.Size} and its payload writes {written} bytes");
        }
    }

    private static int DefaultLength<T>() where T : IUserMessagePayload<T>, new()
        => new T().Write(new byte[UserMessageSender.MaxPayloadBytes]);
}
