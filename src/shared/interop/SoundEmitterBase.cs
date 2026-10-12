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
    private readonly SoundEmitterSystemVtbl* _vtbl;

    private SoundEmitterBase(void* self, void** table)
    {
        _self = self;
        _vtbl = (SoundEmitterSystemVtbl*)table;
    }

    /// <summary>
    /// From the engine's factory, through <paramref name="engine"/>'s own side
    /// (plan_abi_hotreload D8); null, with the reason, when it does not answer.
    /// </summary>
    public static IVtableSoundEmitter? Acquire(ModuleEngine engine, out string why)
        => Vtable.TryAcquire<SoundEmitterSystemVtbl>((engine ?? throw new ArgumentNullException(nameof(engine))).Api.Interfaces, SourceSharpFactoryType.AppSystem,
               InterfaceVersion, out void* self, out void** table, out why)
            ? new SoundEmitterBase(self, table)
            : null;

    public int SoundIndex(string name)
    {
        byte[] bytes = Vtable.Utf8(name);

        fixed (byte* p = bytes)
            return _vtbl->GetSoundIndex(_self, p);
    }

    public string? SoundName(int index) => Vtable.TextOf(_vtbl->GetSoundName(_self, index), 512);
}
