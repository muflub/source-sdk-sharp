namespace SourceSharp.Abi;

/// <summary>
/// The engine's sound script table, <c>ISoundEmitterSystemBase</c> — the two
/// members the server's model components need: whether a script sound exists,
/// and its name as the table spells it. plan_server_novalve.md P3.
/// </summary>
public interface IVtableSoundEmitter
{
    /// <summary>The script sound's index, or -1 when the table has no such sound.</summary>
    int SoundIndex(string name);

    /// <summary>The name at an index, as the table spells it; null for none.</summary>
    string? SoundName(int index);
}

public sealed unsafe class SoundEmitterBase : IVtableSoundEmitter
{
    public const string InterfaceVersion = "VSoundEmitter002";

    private readonly void* _self;
    private readonly SoundEmitterSystemFwd* _fwd;

    private SoundEmitterBase(void* self, SoundEmitterSystemFwd* table)
    {
        _self = self;
        _fwd = table;
    }

    /// <summary>
    /// From the engine's factory, through <paramref name="engine"/>'s own side
    /// (plan_abi_hotreload D8); null, with the reason, when it does not answer.
    /// </summary>
    public static IVtableSoundEmitter? Acquire(ModuleEngine engine, out string why)
        => Forwarders.TryAcquire<SoundEmitterSystemFwd>((engine ?? throw new ArgumentNullException(nameof(engine))).Api.Interfaces, SourceSharpFactoryType.AppSystem,
               InterfaceVersion, out void* self, out SoundEmitterSystemFwd* table, out why)
            ? new SoundEmitterBase(self, table)
            : null;

    public int SoundIndex(string name)
    {
        byte[] bytes = Vtable.Utf8(name);

        fixed (byte* p = bytes)
            return _fwd->GetSoundIndex(_self, p);
    }

    public string? SoundName(int index) => Vtable.TextOf(_fwd->GetSoundName(_self, index), 512);
}
