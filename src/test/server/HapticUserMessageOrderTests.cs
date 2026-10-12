using System.Reflection;
using System.Text.RegularExpressions;
using SourceSharp.Abi;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The six haptics messages are declared under their stock names with stock's
/// sizes.
///
/// <para>
/// They held ids 0-5 for as long as C++ registered them before anything else.
/// Since plan_side_isolation P2b an id is the key's place in key order, carried
/// to every client by the SsUserMessages table, so their position is no longer
/// protocol and is not pinned (UserMessageRoutingTests pins the key order
/// itself). <see cref="Stock"/> still names them, and
/// SourceSharp.OracleTests (HapticUserMessageOrderOracleTests) holds the names
/// and sizes against <c>public/haptics/haptic_msgs.cpp</c>.
/// </para>
/// </summary>
public class HapticUserMessageOrderTests
{
    /// <summary>
    /// <c>RegisterHapticMessages()</c>'s six, in its order, with its sizes.
    /// -1 is a variable-length payload; 0 is an empty one.
    /// </summary>
    public static readonly (string Name, int Size)[] Stock =
    [
        ("SPHapWeapEvent",   4),
        ("HapDmg",          -1),
        ("HapPunch",        -1),
        ("HapSetDrag",      -1),
        ("HapSetConst",     -1),
        ("HapMeleeContact",  0),
    ];

    private static IReadOnlyList<UserMessageDeclaration> Declared()
    {
        var registry = new UserMessageRegistry(new RecordingLog(), receives: false);
        registry.RegisterAll(typeof(FadeMessage).Assembly);
        return registry.Messages;
    }

    [Fact]
    public void EveryHapticIsDeclaredUnderItsBareStockName()
    {
        var declared = Declared();

        foreach ((string name, int _) in Stock)
        {
            var d = Assert.Single(declared, m => m.Name == name);
            Assert.True(d.Type.GetCustomAttribute<UserMessageAttribute>()!.Stock,
                $"{name} must be a Stock declaration: native haptics code hooks it by that name");
        }
    }

    [Fact]
    public void EveryHapticCarriesStocksSize()
    {
        var declared = Declared();

        foreach ((string name, int size) in Stock)
        {
            int got = declared.Single(m => m.Name == name).Size;
            Assert.True(got == size,
                $"{name} is declared Size = {got} and stock registers {size}; "
                + "the engine validates a fixed-size message against this");
        }
    }

    [Fact]
    public void EveryOneOfTheSixHasAPayloadAndAHandler()
    {
        // haptic_msgs.cpp's senders and HookHapticMessages are compiled into
        // neither binary, so the six are managed end to end like every other
        // message (user ruling 2026-10-04): stock's layouts, as payloads.
        var declared = Declared();

        foreach ((string name, int _) in Stock)
        {
            Type type = declared.Single(m => m.Name == name).Type;

            Assert.True(type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IUserMessage<>)),
                $"{name} has no payload handler");
        }
    }
}
