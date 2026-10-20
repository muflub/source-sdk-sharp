namespace SourceSharp.Abi;

/// <summary>
/// The input system's names for buttons, as the key bindings form needs them:
/// a key captured as a ButtonCode_t is bound by name (<c>bind w +forward</c>).
/// </summary>
public interface IFwdInputSystem
{
    /// <summary>inputsystem's name for <paramref name="buttonCode"/> (<c>w</c>, <c>SPACE</c>, <c>MOUSE1</c>), or null.</summary>
    string? ButtonName(int buttonCode);
}

/// <summary>
/// IInputSystem through its forwarders (InputSystemFwd, generated): only
/// ButtonCodeToString is reached. The client's appSystemFactory answers
/// InputSystemVersion001; the server has no input system.
/// </summary>
public sealed unsafe class InputSystem : IFwdInputSystem
{
    public const string InterfaceVersion = "InputSystemVersion001";

    // ButtonCode.h MOUSE_LAST: the UI forwards only keyboard and mouse codes,
    // and ButtonCodeToString indexes a table, so nothing past them is asked.
    private const int MouseLast = 113, MaxName = 64;

    private readonly void* _self;
    private readonly InputSystemFwd* _fwd;

    private InputSystem(void* self, InputSystemFwd* table)
    {
        _self = self;
        _fwd = table;
    }

    /// <summary>The input system, or null with the reason, acquired through the side's factory.</summary>
    internal static IFwdInputSystem? Acquire(NativeInterfaceFactory factory, out string why)
    {
        return Forwarders.TryAcquire<InputSystemFwd>(
                   factory, SourceSharpFactoryType.AppSystem,
                   InterfaceVersion, out void* self, out InputSystemFwd* table, out why)
            ? new InputSystem(self, table)
            : null;
    }

    /// <inheritdoc/>
    public string? ButtonName(int buttonCode)
        => buttonCode is > 0 and <= MouseLast
            ? Vtable.TextOf(_fwd->ButtonCodeToString(_self, buttonCode), MaxName) is { Length: > 0 } name ? name : null
            : null;
}
