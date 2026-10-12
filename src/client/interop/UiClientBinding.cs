using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>What the UI's managed-built area forwards to: the core's UI host.</summary>
public interface IUiClientEvents
{
    void Paint(int layer, int screenWidth, int screenHeight);

    void ScreenSizeChanged(int oldWidth, int oldHeight, int newWidth, int newHeight);

    /// <summary>One input event the menu root received: kind as UiInputKind, position in pixels, key or mouse code, character, wheel delta.</summary>
    void Input(int kind, int x, int y, int code, int character, float delta);

    /// <summary>A viewport panel shown or hidden; true when the UI took it (the stock panel then never sees it).</summary>
    bool ViewportPanel(string name, bool show);
}

/// <summary>The engine's ICenterPrint (VCENTERPRINT002), as the client's shell forwards it.</summary>
public interface ICenterPrintEvents
{
    /// <summary>A centre print in a colour (stock prints white unless asked).</summary>
    void Print(string text, int r, int g, int b, int a);

    /// <summary>Takes the centre print down (a level change).</summary>
    void Clear();
}

/// <summary>The engine facts the loading and paused plaques are decided from (stock VGui_PreRender), on change.</summary>
public interface ILoadingDiscEvents
{
    /// <summary><paramref name="facts"/> a <see cref="PlaqueFacts"/> set; the seconds left on a timed pause, else -1.</summary>
    void State(PlaqueFacts facts, int pausedSeconds);
}

/// <summary>What the engine says each frame that the plaques depend on (the shell's VGui_PreRender, by bit).</summary>
[Flags]
public enum PlaqueFacts
{
    None = 0,
    DrawingLoadingImage = 1,
    PlayingDemo = 2,
    GameUiVisible = 4,
    Paused = 8,
    TakingScreenshot = 16,
}

/// <summary>The engine's IClientDLL::VoiceStatus, as the client's shell forwards it.</summary>
public interface IVoiceStatusEvents
{
    /// <summary>A speaker started or stopped: an entity index, -1 the local player, -2 the server's acknowledgement.</summary>
    void Status(int entIndex, bool talking);
}

/// <summary>The key bindings the client mode's key decision reads; every other binding is <see cref="Other"/>.</summary>
public enum ClientKeyBinding
{
    None,
    Other,
    MessageMode,
    Say,
    MessageMode2,
    SayTeam,
    MessageMode3,
    SayParty,
    Duck,
    Attack,
    Attack2,
    Jump,
    Strafe,
}

/// <summary>The engine's mute questions (IGameClientExports' IsPlayerGameVoiceMuted and its two setters).</summary>
public interface IVoiceBlockEvents
{
    bool IsBlocked(int client);

    /// <summary>Stock's SetPlayerBlockedState, which flips the mute whatever it is asked.</summary>
    void ToggleBlocked(int client);
}

/// <summary>The engine's key events as the client mode is offered them (stock ClientModeShared::KeyInput).</summary>
public interface IClientKeyEvents
{
    /// <summary>1 the engine runs the binding, 0 the key was used, -1 no decision: the HUD elements are asked.</summary>
    int KeyInput(bool down, ClientKeyBinding binding, bool consoleVisible, int maxClients);
}

/// <summary>
/// IManagedUiClient (plan_client_ui §8 U2): the root panels' paint and the
/// screen's size changes, forwarded to the UI host. The host is built after the
/// bindings, so it is found when called; before that the calls do nothing.
/// </summary>
public sealed class UiClientBinding(Crossings crossings, Func<IUiClientEvents?> target,
                                    Func<ICenterPrintEvents?>? centre = null, Func<ILoadingDiscEvents?>? disc = null,
                                    Func<IVoiceStatusEvents?>? voice = null, Func<IClientKeyEvents?>? keys = null,
                                    Func<IVoiceBlockEvents?>? mutes = null) : IManagedUiClient
{
    public int VoiceIsBlocked(int a0)
    {
        crossings.Count(CrossingOp.ClientUiVoiceIsBlocked);
        return mutes?.Invoke()?.IsBlocked(a0) == true ? 1 : 0;
    }

    public void VoiceToggleBlocked(int a0)
    {
        crossings.Count(CrossingOp.ClientUiVoiceToggleBlocked);
        mutes?.Invoke()?.ToggleBlocked(a0);
    }

    /// <summary>No decision: the shell asks the HUD elements, then lets the engine run the binding.</summary>
    public const int KeyNoDecision = -1;

    public unsafe int KeyInput(int a0, int a1, byte* a2, int a3, int a4)
    {
        crossings.Count(CrossingOp.ClientUiKeyInput);
        if (keys?.Invoke() is not { } k)
            return KeyNoDecision;
        return k.KeyInput(a0 != 0, BindingOf(a2), a3 != 0, a4);
    }

    /// <summary>The binding's name read without making a string: a key press is not worth an allocation.</summary>
    public static unsafe ClientKeyBinding BindingOf(byte* name)
    {
        if (name == null)
            return ClientKeyBinding.None;
        var b = System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpanFromNullTerminated(name);
        return BindingOf(b);
    }

    public static ClientKeyBinding BindingOf(ReadOnlySpan<byte> b)
    {
        if (b.IsEmpty) return ClientKeyBinding.None;
        if (b.SequenceEqual("messagemode"u8)) return ClientKeyBinding.MessageMode;
        if (b.SequenceEqual("say"u8)) return ClientKeyBinding.Say;
        if (b.SequenceEqual("messagemode2"u8)) return ClientKeyBinding.MessageMode2;
        if (b.SequenceEqual("say_team"u8)) return ClientKeyBinding.SayTeam;
        if (b.SequenceEqual("messagemode3"u8)) return ClientKeyBinding.MessageMode3;
        if (b.SequenceEqual("say_party"u8)) return ClientKeyBinding.SayParty;
        if (b.SequenceEqual("+duck"u8)) return ClientKeyBinding.Duck;
        if (b.SequenceEqual("+attack"u8)) return ClientKeyBinding.Attack;
        if (b.SequenceEqual("+attack2"u8)) return ClientKeyBinding.Attack2;
        if (b.SequenceEqual("+jump"u8)) return ClientKeyBinding.Jump;
        if (b.SequenceEqual("+strafe"u8)) return ClientKeyBinding.Strafe;
        return ClientKeyBinding.Other;
    }

    public void Paint(int a0, int a1, int a2)
    {
        crossings.Count(CrossingOp.ClientUiPaint);
        target()?.Paint(a0, a1, a2);
    }

    public void ScreenSizeChanged(int a0, int a1, int a2, int a3)
    {
        crossings.Count(CrossingOp.ClientUiScreenSizeChanged);
        target()?.ScreenSizeChanged(a0, a1, a2, a3);
    }

    public void Input(int a0, int a1, int a2, int a3, int a4, float a5)
    {
        crossings.Count(CrossingOp.ClientUiInput);
        target()?.Input(a0, a1, a2, a3, a4, a5);
    }

    public unsafe int ViewportPanel(byte* a0, int a1)
    {
        crossings.Count(CrossingOp.ClientUiViewportPanel);
        if (a0 == null || target() is not { } t)
            return 0;
        return t.ViewportPanel(new string((sbyte*)a0), a1 != 0) ? 1 : 0;
    }

    public unsafe void CenterPrint(byte* a0, int a1, int a2, int a3, int a4)
    {
        crossings.Count(CrossingOp.ClientUiCenterPrint);
        if (a0 != null && centre?.Invoke() is { } c)
            c.Print(System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)a0) ?? string.Empty, a1, a2, a3, a4);
    }

    public void PlaqueState(int a0, int a1)
    {
        crossings.Count(CrossingOp.ClientUiPlaqueState);
        disc?.Invoke()?.State((PlaqueFacts)a0, a1);
    }

    public void VoiceStatus(int a0, int a1)
    {
        crossings.Count(CrossingOp.ClientUiVoiceStatus);
        voice?.Invoke()?.Status(a0, a1 != 0);
    }

    public void CenterClear()
    {
        crossings.Count(CrossingOp.ClientUiCenterClear);
        centre?.Invoke()?.Clear();
    }
}
