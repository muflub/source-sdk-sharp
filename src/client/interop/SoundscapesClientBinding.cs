using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// IManagedSoundscapesClient: the native client-system shell's three calls,
/// each counted, each landing on the driver.
/// </summary>
public sealed unsafe class SoundscapesClientBinding(Crossings crossings, IClientSoundscapesDriver driver) : IManagedSoundscapesClient
{
    public void ClientSoundscapesLevelInit(byte* a0)
    {
        crossings.Count(CrossingOp.ClientSoundscapesLevelInit);
        driver.LevelInit(System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)a0) ?? string.Empty);
    }

    public void ClientSoundscapesUpdate(float a0)
    {
        crossings.Count(CrossingOp.ClientSoundscapesUpdate);
        driver.Update(a0);
    }

    public void ClientSoundscapesLevelShutdown()
    {
        crossings.Count(CrossingOp.ClientSoundscapesLevelShutdown);
        driver.LevelShutdown();
    }
}
