namespace SourceSharp.Abi;

//=============================================================================
// IVEngineServer's wrapper, the server's alone (plan_engine_forwarders.md, one
// library per side, ruled 2026-10-18). It holds the forwarder tables of
// IVEngineServer and of the INetChannelInfo objects GetPlayerNetInfo answers,
// which libss_fwd_server serves and the client's library does not, so it lives
// in Server.Interop beside them. The interface it implements, and what to
// know before adding a method to it, are Shared.Interop's
// (IVtableEngineServer.cs): the shared bindings ask the interface, through the
// side's bound slot, which EngineServerBinding fills on the server.
//=============================================================================

/// <summary>
/// <see cref="IVtableEngineServer"/> over the engine's own object, called
/// through its vtable.
/// </summary>
public sealed unsafe class EngineServer : IVtableEngineServer, SourceSharp.Server.Interop.ISaveMemory
{
    /// <summary><see cref="IVtableEngineServer.InterfaceVersion"/>.</summary>
    public const string InterfaceVersion = IVtableEngineServer.InterfaceVersion;

    private readonly void* _self;
    private readonly EngineServerFwd* _fwd;

    // The forwarders of the INetChannelInfo objects GetPlayerNetInfo answers.
    private readonly NetChannelInfoFwd* _netInfo;

    private EngineServer(void* self, EngineServerFwd* table, NetChannelInfoFwd* netInfo)
    {
        _self = self;
        _fwd = table;
        _netInfo = netInfo;
    }

    /// <summary>
    /// The engine server, or null with the reason. Null on the client, which is
    /// not an error: this interface exists only on the server.
    ///
    /// <para>
    /// Through <paramref name="engine"/>'s own side's interface factory
    /// (plan_abi_hotreload D8), never the process-wide one that followed the
    /// thread's active side.
    /// </para>
    /// </summary>
    public static IVtableEngineServer? Acquire(ModuleEngine engine, out string why)
        => Acquire((engine ?? throw new ArgumentNullException(nameof(engine))).Api.Interfaces, out why);

    /// <summary>
    /// The same, through one side's own interface factory (plan_abi_hotreload
    /// D8): what <see cref="EngineBound"/> holds per side.
    /// </summary>
    internal static IVtableEngineServer? Acquire(NativeInterfaceFactory factory, out string why)
        => Forwarders.TryAcquire<EngineServerFwd>(
               factory, SourceSharpFactoryType.AppSystem,
               InterfaceVersion, out void* self, out EngineServerFwd* table, out why)
           && Forwarders.TryAcquire(factory, out NetChannelInfoFwd* netInfo, out why)
            ? new EngineServer(self, table, netInfo)
            : null;

    /// <inheritdoc/>
    public string? GameDir() => Fill(_fwd->GetGameDir, _self, Vtable.MaxPath);

    /// <inheritdoc/>
    public string? MapEntitiesString()
        => Vtable.TextOf(_fwd->GetMapEntitiesString(_self), Vtable.MaxEntityString);

    /// <inheritdoc/>
    public bool IsModelPrecached(string path) => Ask(_fwd->IsModelPrecached, _self, path);

    /// <inheritdoc/>
    public bool IsDedicatedServer() => _fwd->IsDedicatedServer(_self) != 0;

    /// <summary>IVEngineServer::SaveAllocMemory, for the managed SaveGameSession (T4c).</summary>
    void* SourceSharp.Server.Interop.ISaveMemory.SaveAlloc(nuint count, nuint size) => _fwd->SaveAllocMemory(_self, count, size);

    /// <summary>IVEngineServer::SaveFreeMemory.</summary>
    void SourceSharp.Server.Interop.ISaveMemory.SaveFree(void* block) => _fwd->SaveFreeMemory(_self, block);

    /// <inheritdoc/>
    public int EntityCount() => _fwd->GetEntityCount(_self);

    /// <inheritdoc/>
    public int PlayerUserId(int entIndex)
    {
        void* edict = _fwd->PEntityOfEntIndex(_self, entIndex);
        if (edict == null)
            return 0;

        int userId = _fwd->GetPlayerUserId(_self, edict);
        return userId < 0 ? 0 : userId;
    }

    /// <inheritdoc/>
    public void ServerCommandNow(string command)
    {
        fixed (byte* p = Vtable.Utf8(command + "\n"))
            _fwd->ServerCommand(_self, p);
        _fwd->ServerExecute(_self);
    }

    /// <inheritdoc/>
    public void SetView(int playerIndex, int viewIndex)
    {
        void* player = _fwd->PEntityOfEntIndex(_self, playerIndex);
        if (player == null)
            return;

        void* view = viewIndex >= 0 ? _fwd->PEntityOfEntIndex(_self, viewIndex) : null;
        _fwd->SetView(_self, player, view != null ? view : player);
    }

    /// <inheritdoc/>
    public void LightStyle(int style, string pattern)
    {
        fixed (byte* p = Vtable.Utf8(pattern))
            _fwd->LightStyle(_self, style, p);
    }

    /// <inheritdoc/>
    public void EmitAmbientSound(int entIndex, Vector origin, string sample, float volume, int soundLevel,
                                 int flags, int pitch, float soundTime)
    {
        fixed (byte* p = Vtable.Utf8(sample))
            _fwd->EmitAmbientSound(_self, entIndex, &origin, p, volume, soundLevel, flags, pitch, soundTime);
    }

    /// <inheritdoc/>
    public int SentenceIndexFromName(string name)
    {
        fixed (byte* p = Vtable.Utf8(name))
            return _fwd->SentenceIndexFromName(_self, p);
    }

    /// <inheritdoc/>
    public void MulticastRecipients(bool usePas, Vector origin, Span<uint> playerBits)
    {
        if (playerBits.Length < IVtableEngineServer.PlayerBitWords)
            throw new ArgumentException(
                $"the PAS is {IVtableEngineServer.PlayerBitWords} words (CBitVec<ABSOLUTE_PLAYER_LIMIT>); {playerBits.Length} were given",
                nameof(playerBits));

        playerBits[..IVtableEngineServer.PlayerBitWords].Clear();

        fixed (uint* bits = playerBits)
            _fwd->Message_DetermineMulticastRecipients(_self, usePas ? (byte)1 : (byte)0, &origin, bits);
    }

    /// <inheritdoc/>
    public void PlaybackTempEntity(nint filter, float delay, ReadOnlySpan<byte> sender, nint sendTable, int classId)
    {
        if (filter == 0 || sendTable == 0 || sender.IsEmpty)
            return;

        fixed (byte* p = sender)
            _fwd->PlaybackTempEntity(_self, (void*)filter, delay, p, (void*)sendTable, classId);
    }

    /// <summary>MAX_MAP_LEAFS / 8 (bspfile.h): the PVS buffer stock hands the engine.</summary>
    private const int PvsBytes = 65536 / 8;

    /// <inheritdoc/>
    public bool BoxInPvs(Vector eye, Vector mins, Vector maxs)
    {
        int cluster = _fwd->GetClusterForOrigin(_self, &eye);

        byte* pvs = stackalloc byte[PvsBytes];
        int bytes = _fwd->GetPVSForCluster(_self, cluster, PvsBytes, pvs);
        if (bytes <= 0)
            return false;

        return _fwd->CheckBoxInPVS(_self, &mins, &maxs, pvs, bytes) != 0;
    }

    /// <inheritdoc/>
    public ulong ClientSteamId(int entIndex)
    {
        // A CSteamID is one 64-bit word (its bitfields are a union with
        // m_unAll64Bits), so the pointer reads as the SteamID64 itself.
        void* steamId = _fwd->GetClientSteamIDByPlayerIndex(_self, entIndex);
        return steamId is null ? 0 : *(ulong*)steamId;
    }

    public string? ClientAddress(int entIndex)
    {
        void* channel = _fwd->GetPlayerNetInfo(_self, entIndex);
        if (channel is null)
            return null;

        return Vtable.TextOf(_netInfo->GetAddress(channel), MaxAddress);
    }

    public float? ClientAvgLatency(int entIndex)
    {
        void* channel = _fwd->GetPlayerNetInfo(_self, entIndex);
        if (channel is null)
            return null;

        return _netInfo->GetAvgLatency(channel, 0);   // FLOW_OUTGOING
    }

    public string? ClientConVarValue(int entIndex, string name)
    {
        byte[] bytes = Vtable.Utf8(name);
        fixed (byte* p = bytes)
            return Vtable.TextOf(_fwd->GetClientConVarValue(_self, entIndex, p), MaxConVarValue);
    }

    private const int MaxConVarValue = 256;

    // "[ipv6]:port" is 47 bytes; the engine's own netadr text is shorter.
    private const int MaxAddress = 64;

    public int GetArea(Vector origin)
    {
        // `const Vector &` IS A POINTER, which is the whole of what the ABI
        // says about a reference -- the generated field spells it void* for
        // that reason. Three floats on the stack, and the engine reads them
        // during the call and does not keep them.
        Vector local = origin;

        return _fwd->GetArea(_self, &local);
    }

    //-------------------------------------------------------------------------
    // TWO SHAPES THAT ARE NOT ONE-LINERS AND SO ARE HELPERS, not members with a
    // body. `GetGameDir` writes into a caller's buffer and `IsModelPrecached`
    // takes a string, and neither is expressible as a single expression -- so
    // the shape lives here once and the member above stays a delegation.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A <c>void ( char *, int )</c> slot that fills a caller's buffer.
    ///
    /// <para>
    /// THE BUFFER IS ZEROED BY THE RUNTIME AND THAT IS LOAD-BEARING: a callee
    /// that writes nothing leaves an empty string rather than whatever was
    /// there. And it is scanned rather than read with
    /// <see cref="Vtable.TextOf"/>, which would answer null for a full buffer
    /// with no terminator where this answers its contents.
    /// </para>
    /// </summary>
    private static string Fill(
        delegate* unmanaged<void*, byte*, int, void> slot, void* self, int size)
    {
        var buffer = new byte[size];

        fixed (byte* p = buffer)
        {
            slot(self, p, size);

            int length = 0;

            while (length < size && p[length] != 0)
                length++;

            return System.Text.Encoding.UTF8.GetString(p, length);
        }
    }

    /// <summary>A <c>bool ( const char * )</c> slot.</summary>
    private static bool Ask(
        delegate* unmanaged<void*, byte*, byte> slot, void* self, string text)
    {
        byte[] bytes = Vtable.Utf8(text);

        fixed (byte* p = bytes)
            return slot(self, p) != 0;
    }
}
