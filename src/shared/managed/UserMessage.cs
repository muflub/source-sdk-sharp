using System.Reflection;

namespace SourceSharp.Abi;

/// <summary>
/// Marks a class as a Source user message.
///
/// A message travels as a one-byte engine id. Since plan_side_isolation P2b the
/// id is the message's position in the ordinal order of the KEYS
/// (<see cref="UserMessageKey"/>), fixed by the server, and every client reads
/// each id's key from the server-written SsUserMessages table
/// (<see cref="UserMessageTable"/>). Declaration order and the assembly a
/// message is declared in no longer mean anything on the wire.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class UserMessageAttribute : Attribute
{
    public UserMessageAttribute(string name)
    {
        Name = name;
    }

    /// <summary>
    /// The message's id. For a <see cref="Stock"/> message it is the name
    /// Valve code knows it by, e.g. "Fade"; for any other it only has to be
    /// unique within its module, because the key it travels under is
    /// <c>&lt;module&gt;:&lt;id&gt;</c> (<see cref="UserMessageKey"/>).
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// A message Valve's code names (hooks, senders): it keeps its bare name
    /// as its key, and must be on <see cref="UserMessageKey.StockNames"/>.
    /// </summary>
    public bool Stock { get; init; }

    /// <summary>Payload size in bytes, or -1 for variable length.</summary>
    public int Size { get; init; } = -1;
}

/// <summary>
/// A user message the client can receive.
///
/// <para>
/// Receiving is client-only: the server writes these, the client reads them, and
/// <see cref="OnReceived"/> is never called on the server side. Declaring the
/// class in shared code is still required so both sides agree on the ordering.
/// </para>
///
/// <para>
/// An interface rather than a base class, and OPTIONAL. Declaring a message and
/// handling one are different jobs: a message a native HUD element reads needs a
/// managed declaration so the index is right and no managed handler at all. A
/// <c>[UserMessage]</c> class without this interface is the accurate statement of
/// that — <i>this message exists on the wire and managed code does not read it</i>
/// — where under a mandatory base class it had to be an empty override that reads
/// as an unimplemented feature. It also gives the one base-class slot back, so a
/// handler can be a HUD element or a lifecycle participant as well.
/// </para>
/// </summary>
public interface IUserMessage
{
    /// <summary>
    /// Called on the client when the message arrives. False when the payload
    /// was refused (it ended mid-member or was malformed) and nothing was
    /// dispatched; whoever dispatched it says so, under the message's key.
    /// </summary>
    bool OnReceived(UserMessageReader reader);
}

/// <summary>
/// A user message's payload: a <c>partial</c> struct or class whose
/// <c>partial</c> properties are its members, in declaration order. The
/// UserMessagePayloadGenerator implements all of this from the declaration,
/// once a <c>[UserMessage]</c> handler names the payload through
/// <see cref="IUserMessage{TPayload}"/>; nothing here is written by hand.
///
/// <para>
/// One wire convention for every message: declaration order, no header;
/// fixed-width little-endian (bool and byte 1, short and ushort 2, int, uint
/// and float 4, <see cref="Color32"/> 4 as r g b a, <see cref="Vector"/> 12);
/// a string NUL-terminated; an array a count byte, then its elements.
/// </para>
/// </summary>
public interface IUserMessagePayload<TSelf> where TSelf : IUserMessagePayload<TSelf>
{
    /// <summary>
    /// The handler that names this payload. Its <see cref="UserMessageAttribute"/>
    /// gives the key the payload is sent under, so a sender never builds a
    /// handler or spells a key.
    /// </summary>
    static abstract Type Handler { get; }

    /// <summary>Writes the members into <paramref name="into"/>: the length written, or -1 when they do not fit.</summary>
    int Write(Span<byte> into);

    /// <summary>
    /// Reads the members back. A payload that ended mid-member or was
    /// malformed leaves <see cref="UserMessageReader.Overran"/> set, and is not
    /// a message.
    /// </summary>
    static abstract TSelf Read(ref UserMessageReader reader);
}

/// <summary>
/// A handler that takes its message as a typed payload: the receiving half of
/// the payload model.
///
/// <para>
/// The untyped <see cref="IUserMessage.OnReceived"/> the registry dispatches
/// through is this interface's own: it reads a <typeparamref name="TPayload"/>
/// and, unless the payload overran or was malformed, hands it to
/// <see cref="OnReceived(TPayload)"/>. A refused payload answers false and
/// nothing is dispatched; the registry or the receiver that dispatched it
/// says so through its composition's log.
/// </para>
/// </summary>
public interface IUserMessage<TPayload> : IUserMessage where TPayload : IUserMessagePayload<TPayload>
{
    /// <summary>Called on the client with each message that read whole.</summary>
    void OnReceived(TPayload message);

    bool IUserMessage.OnReceived(UserMessageReader reader)
    {
        TPayload message = TPayload.Read(ref reader);

        if (reader.Overran)
            return false;

        OnReceived(message);
        return true;
    }
}

/// <summary>
/// The most a string member is written with: longer is cut to
/// <see cref="MaxLength"/> UTF-16 code units on WRITE (a reader takes what
/// arrived). Without it a string is written whole, and the sender refuses a
/// payload over the engine's 255-byte limit.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class UserMessageStringAttribute : Attribute
{
    /// <summary>The longest text written, in UTF-16 code units.</summary>
    public int MaxLength { get; init; } = -1;
}

/// <summary>
/// The most elements an array member carries, 1 to 255 (its count byte's
/// range); required on every array member. Longer is cut on write; a count
/// over it on read refuses the payload.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class UserMessageArrayAttribute : Attribute
{
    /// <summary>The most elements written or accepted.</summary>
    public int MaxCount { get; init; } = -1;
}

/// <summary>Sends a payload under the key its handler declares.</summary>
public static class UserMessagePayloadSending
{

    /// <summary>
    /// Writes <paramref name="payload"/> and sends it under its handler's key
    /// (<see cref="IUserMessagePayload{TSelf}.Handler"/>) to
    /// <paramref name="player"/>'s slot, or to everyone for 0. False when it
    /// does not fit the engine's limit, and nothing is sent: the CALLER says
    /// so, through its own log, naming what it was sending (this has none).
    /// A payload whose members are all bounded cannot fail. The sender's other
    /// refusals (a key this side does not register, an empty slot) are said by
    /// the sender.
    /// </summary>
    public static bool Send<TPayload>(this IUserMessageSender sender, TPayload payload, int player = 0, bool reliable = true)
        where TPayload : IUserMessagePayload<TPayload>
    {
        ArgumentNullException.ThrowIfNull(sender);

        string key = UserMessageKey.Of(TPayload.Handler);
        Span<byte> buffer = stackalloc byte[UserMessageSender.MaxPayloadBytes];

        int length = payload.Write(buffer);
        if (length < 0)
            return false;

        sender.Send(key, buffer[..length], player, reliable);
        return true;
    }
}

/// <summary>
/// A user message handler's typed event, as a subscriber takes it through its
/// constructor: raised on the receiving side once a message has decoded.
///
/// <para>
/// The handlers are instances held by the client module's container
/// (<see cref="UserMessageRegistry.AddHandlersTo"/>). Their events used to be
/// static, which made every subscriber process-wide.
/// </para>
/// </summary>
public interface IUserMessageEvent<TArgs>
{
    /// <summary>Raised once per decoded message.</summary>
    event Action<TArgs>? Fired;
}

/// <summary>
/// One registered message: its name, its size, its wire index, and the handler
/// if it has one.
///
/// <para>
/// The handler is nullable because most messages do not have one, and because
/// the server never has one at all. What every entry must carry is the name and
/// the position, because the position IS the wire protocol.
/// </para>
/// </summary>
public sealed class UserMessageDeclaration
{
    public UserMessageDeclaration(string name, int size, int index, Type type, IUserMessage? handler)
    {
        Name    = name;
        Size    = size;
        Index   = index;
        Type    = type;
        Handler = handler;
    }

    /// <summary>Message name as the engine knows it.</summary>
    public string Name { get; }

    /// <summary>Payload size in bytes, or -1 for variable length.</summary>
    public int Size { get; }

    /// <summary>Registration position, which is what travels on the wire.</summary>
    public int Index { get; }

    /// <summary>The declaring class.</summary>
    public Type Type { get; }

    /// <summary>The receiver, or null for a declaration nothing managed reads.</summary>
    public IUserMessage? Handler { get; }

    /// <summary>True when managed code will be handed this message's payload.</summary>
    public bool Dispatched => Handler is not null;

    private long _received;

    /// <summary>
    /// How many of these this side has received, handled or not: what the dev
    /// API reports so a live check can see a message arrive.
    /// </summary>
    public long Received => Interlocked.Read(ref _received);

    internal void NoteReceived() => Interlocked.Increment(ref _received);
}

/// <summary>
/// Reads a user message payload.
///
/// Native copies the message bytes across the ABI rather than handing over a
/// live bf_read, so nothing here can outlive the dispatch or read engine memory
/// out from under itself. Every read is bounds-checked and returns a default
/// past the end rather than throwing, because a malformed message from the
/// network must not take down the managed runtime.
/// </summary>
public ref struct UserMessageReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _pos;

    public UserMessageReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _pos  = 0;
    }

    /// <summary>Bytes not yet consumed.</summary>
    public readonly int Remaining => _data.Length - _pos;

    /// <summary>
    /// True once a read has run past the end of the payload, or the payload
    /// was refused as malformed (<see cref="Refuse"/>).
    /// </summary>
    public bool Overran { get; private set; }

    /// <summary>
    /// Marks the payload malformed: a member read whole but out of its
    /// declared range (an array count over its most). Read as an overrun.
    /// </summary>
    public void Refuse() => Overran = true;

    public byte ReadByte()
    {
        if (_pos + 1 > _data.Length) { Overran = true; return 0; }
        return _data[_pos++];
    }

    public short ReadShort()
    {
        if (_pos + 2 > _data.Length) { Overran = true; return 0; }
        short v = BitConverter.ToInt16(_data.Slice(_pos, 2));
        _pos += 2;
        return v;
    }

    public int ReadInt()
    {
        if (_pos + 4 > _data.Length) { Overran = true; return 0; }
        int v = BitConverter.ToInt32(_data.Slice(_pos, 4));
        _pos += 4;
        return v;
    }

    public float ReadFloat()
    {
        if (_pos + 4 > _data.Length) { Overran = true; return 0f; }
        float v = BitConverter.ToSingle(_data.Slice(_pos, 4));
        _pos += 4;
        return v;
    }

    /// <summary>Reads a NUL-terminated string. An unterminated run reads to the end.</summary>
    public string ReadString()
    {
        int start = _pos;
        while (_pos < _data.Length && _data[_pos] != 0)
            _pos++;

        var text = System.Text.Encoding.UTF8.GetString(_data.Slice(start, _pos - start));

        if (_pos < _data.Length) _pos++;    // consume the terminator
        else                     Overran = true;

        return text;
    }
}

/// <summary>
/// Discovers <see cref="UserMessageAttribute"/> classes and gives each an engine
/// id in key order: the side's only user-message registry (plan_side_isolation
/// §2.2a; nothing is registered with native code).
///
/// Mirrors <see cref="ConCommandRegistry"/>, with one extra rule: a duplicate
/// key or an unregisterable class is a hard error rather than a skipped entry.
/// </summary>
public sealed class UserMessageRegistry : IUserMessageCatalog
{
    private readonly List<UserMessageDeclaration?> _byIndex = new();
    private readonly Dictionary<string, int> _byKey = new(StringComparer.Ordinal);
    private readonly bool _receives;

    /// <summary>
    /// <paramref name="receives"/> is false on the server, and that is not a
    /// micro-optimisation. Receiving is client-only -- the server writes user
    /// messages and never reads one -- so a server that constructs handlers has
    /// built one object per message to hold a name it will never call, and has
    /// run each handler's constructor on a side its author never intended.
    ///
    /// <para>
    /// The registry is the only store (plan_side_isolation §2.2a): nothing is
    /// registered with native code. The engine asks it for a message's name and
    /// size (GetUserMessageInfo), native senders for an id by name, and the
    /// side's sender and receiver go through it.
    /// </para>
    /// </summary>
    public UserMessageRegistry(ILog log, bool receives = true)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _receives = receives;
    }

    private readonly ILog _log;

    /// <summary>The engine id <paramref name="key"/> travels under, or -1 when nothing declares it.</summary>
    public int IdOf(string key) => _byKey.TryGetValue(key, out int id) ? id : -1;

    /// <summary>The declaration at engine id <paramref name="id"/>, or null for an id the registry does not hold.</summary>
    public UserMessageDeclaration? At(int id) => id >= 0 && id < _byIndex.Count ? _byIndex[id] : null;

    /// <inheritdoc/>
    public bool TryDescribe(int id, out string key, out int size)
    {
        var declaration = At(id);
        key  = declaration?.Name ?? string.Empty;
        size = declaration?.Size ?? -1;
        return declaration is not null;
    }

    /// <summary>The declaration registered under <paramref name="key"/>, or null.</summary>
    public UserMessageDeclaration? ByKey(string key) => At(IdOf(key));

    /// <summary>
    /// The server writes the key of engine id <i>i</i> at index <i>i</i> of the
    /// freshly created SsUserMessages table (plan_side_isolation §2.2), from
    /// <c>CreateNetworkStringTables</c>, the one window the engine allows.
    /// Answers how many keys landed at their id; a key that landed anywhere
    /// else is said, because every client would misroute that message. Seals
    /// the registry against later registration.
    /// </summary>
    public int WriteTable(IEngineStringTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        TableWritten = true;

        int written = 0;
        for (int id = 0; id < _byIndex.Count; id++)
        {
            // A hole (an id an adopted table held and this build does not
            // declare) keeps its place under a key of its own, which no
            // declaration can spell (a stock name has no '#', a module key no
            // leading one), so every later key still lands at its own id.
            string key = _byIndex[id]?.Name ?? $"#retired{id}";
            int index = table.AddString(key);

            if (index == id)
                written++;
            else
                _log.Warning($"[SourceSharp] user message '{key}' landed at index {index} of " +
                            $"{table.Name}, not at its id {id}; a client would misroute it\n");
        }

        _log.Msg($"[SourceSharp] user message table '{table.Name}' id {table.Id}: " +
                $"{written} of {_byIndex.Count} key(s) at their id\n");
        return written;
    }

    /// <summary>
    /// A reloaded server module meets the table the map already sent (F5): the
    /// clients map ids through it, so the ids are the table's. A declaration
    /// the table holds takes the table's id; one it does not hold is refused
    /// until the next restart, and said; an id the table holds and nothing
    /// declares any more stays a hole. Answers how many declarations kept an
    /// id, or -1 when no table exists yet (the first load: nothing to adopt).
    /// </summary>
    public int AdoptTable(IEngineStringTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (table.Id < 0)
            return -1;

        int count = table.Count;
        if (count <= 0)
            return -1;

        var declared = _byIndex.OfType<UserMessageDeclaration>().ToDictionary(d => d.Name, StringComparer.Ordinal);
        var adopted = new List<UserMessageDeclaration?>(count);

        for (int id = 0; id < count; id++)
        {
            string? key = table.GetString(id);
            adopted.Add(key is not null && declared.Remove(key, out var d)
                ? new UserMessageDeclaration(d.Name, d.Size, id, d.Type, d.Handler)
                : null);
        }

        foreach (var refused in declared.Keys.Order(StringComparer.Ordinal))
            _log.Warning($"[SourceSharp] user message '{refused}' was declared after the " +
                        $"{table.Name} table was sent; refused until the server restarts\n");

        _byIndex.Clear();
        _byIndex.AddRange(adopted);
        _byKey.Clear();
        foreach (var d in adopted.OfType<UserMessageDeclaration>())
            _byKey.Add(d.Name, d.Index);

        TableWritten = true;
        return _byKey.Count;
    }

    /// <summary>
    /// Messages registered, in id order. A server registry that adopted a live
    /// table (<see cref="AdoptTable"/>) can hold ids with no declaration; those
    /// are not listed, so a declaration's <see cref="UserMessageDeclaration.Index"/>
    /// is its id and not always its position here.
    /// </summary>
    public IReadOnlyList<UserMessageDeclaration> Messages => _byIndex.OfType<UserMessageDeclaration>().ToList();

    /// <summary>One past the highest id the registry holds: the size of the id space.</summary>
    public int IdCount => _byIndex.Count;

    /// <summary>
    /// True once <see cref="WriteTable"/> or <see cref="AdoptTable"/> has tied
    /// the ids to the SsUserMessages table; a registration after it is refused.
    /// </summary>
    public bool TableWritten { get; private set; }

    /// <summary>How many of the registered declarations were instantiated.</summary>
    public int HandlerCount
    {
        get
        {
            int n = 0;
            foreach (var d in _byIndex)
                if (d is { Dispatched: true }) n++;
            return n;
        }
    }

    /// <summary>
    /// Registers every attributed message class in every assembly loaded beside
    /// <paramref name="assembly"/>, which is expected to be the assembly both
    /// sides share — a message found anywhere else is warned about, because the
    /// other side will not have it. Returns the number registered.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// If a class cannot be registered, rather than skipping it — dropping one
    /// would shift the wire index of every message after it.
    /// </exception>
    /// <param name="assembly">The assembly both sides share.</param>
    /// <param name="services">
    /// Where a receiving registry finds its handlers: the instance
    /// <see cref="AddHandlersTo"/> put there, which is the one the module's
    /// subscribers took through their constructors. A handler the container
    /// does not hold (or no container) is built with its parameterless
    /// constructor, as every handler was before the container built them.
    /// </param>
    public int RegisterAll(Assembly assembly, IServiceLocator? services = null)
        => Register(LoadedAssemblies.Types(assembly, _log), assembly, services);

    /// <summary>
    /// Registers exactly the declarations among <paramref name="types"/>, in
    /// the order given (shared assembly first): what <see cref="RegisterAll"/>
    /// does after its scan. A test process that loads both sides' copies of a
    /// mod's shared source hands one side's here.
    /// </summary>
    internal int Register(IEnumerable<Type> types, Assembly assembly, IServiceLocator? services = null)
    {
        // THE LATE-REGISTRATION REFUSAL (plan_side_isolation §2.2, risk R4):
        // the table the clients map ids through is written once per map from
        // this list, so a message registered after it would arrive at a client
        // that has no entry for it. Registration happens at DLL init; after the
        // table is written it is an error, never a race.
        if (TableWritten)
            throw new InvalidOperationException(
                $"user messages registered after the {UserMessageTables.Name} table was written: " +
                "a client maps every id through that table, so a message added now has no entry " +
                "there. Declare it with the others; registration happens at DLL init.");

        var declared = new List<(UserMessageAttribute Attr, Type Type, string Key)>();

        foreach (var type in types)
        {
            var attr = type.GetCustomAttribute<UserMessageAttribute>();
            if (attr is null)
                continue;

            // The key it registers and is sent under (plan_side_isolation §2.2):
            // a stock message's bare name, any other's <module>:<id>.
            string key = UserMessageKey.Of(type, attr);

            if (type.IsAbstract || type.IsInterface)
                throw new InvalidOperationException(
                    $"[UserMessage({attr.Name})] on abstract type {type.Name}: cannot be instantiated, " +
                    "and skipping it would shift every later message index");

            // No base-class check. The attribute alone makes a declaration: a
            // message a native reader owns needs its NAME and its POSITION in
            // this list and nothing else, and demanding an interface it will
            // never be called through would only produce empty methods.

            declared.Add((attr, type, key));
        }

        // KEY ORDER (plan_side_isolation §2.1, §2.2): a message's engine id is
        // its position in the ordinal order of the keys, whichever assembly
        // declared it and in whatever order the scan met it. The order is
        // the server's alone to fix -- the client reads each id's key from the
        // SsUserMessages table (UserMessageReceiver) -- but sorting both sides
        // the same way keeps a registry's ids a pure function of its keys.
        declared.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));

        RefuseDuplicateKeys(declared.Select(d => (d.Type, d.Key)));

        _byIndex.Clear();
        _byKey.Clear();

        foreach (var (attr, type, key) in declared)
        {
            // Instantiated only where it will actually be dispatched to: a
            // declaration with no IUserMessage has nothing to construct, and
            // neither does anything at all on a side that never receives.
            IUserMessage? handler = null;

            if (_receives && typeof(IUserMessage).IsAssignableFrom(type))
            {
                handler = services?.Resolve(type) as IUserMessage
                          ?? Activator.CreateInstance(type) as IUserMessage;

                if (handler is null)
                    throw new InvalidOperationException(
                        $"could not instantiate user message handler {type.Name}");
            }

            _byKey.Add(key, _byIndex.Count);
            _byIndex.Add(new UserMessageDeclaration(key, attr.Size, _byIndex.Count, type, handler));
        }

        _log.Msg($"[SourceSharp] registered {_byIndex.Count} user message(s) in key order, " +
                   $"{HandlerCount} handled\n");
        return _byIndex.Count;
    }

    /// <summary>
    /// Refuses two declarations under one key, naming both classes: an id must
    /// be unique within its module, and a stock name everywhere.
    /// </summary>
    internal static void RefuseDuplicateKeys(IEnumerable<(Type Type, string Key)> declared)
    {
        var seen = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var (type, key) in declared)
        {
            if (!seen.TryAdd(key, type))
                throw new InvalidOperationException(
                    $"duplicate user message '{key}' on {seen[key].AssemblyQualifiedName} and {type.AssemblyQualifiedName}: " +
                    "an id must be unique within its module (and a stock name everywhere)");
        }
    }

    /// <summary>
    /// Dispatches a received message. Out-of-range indices are ignored rather
    /// than throwing: the index comes off the wire, so it is not trusted.
    /// </summary>
    /// <summary>
    /// Builds every attributed HANDLER (a message class implementing
    /// <see cref="IUserMessage"/>) in every assembly loaded beside
    /// <paramref name="assembly"/> through <paramref name="services"/>, and
    /// registers it under its own type and under every
    /// <see cref="IUserMessageEvent{TArgs}"/> it implements. Returns the number
    /// added.
    ///
    /// <para>
    /// Separate from <see cref="RegisterAll"/>, which fixes the wire indices in
    /// the DLL-init window and must stay where it is, because the subscribers
    /// — the HUD, the module's own wiring — are built earlier and take the
    /// handlers through their constructors. Makes no engine call, and changes
    /// no index: the declaration order is still <see cref="RegisterAll"/>'s.
    /// </para>
    ///
    /// <para>
    /// The container builds them, so a handler may take what it needs (an
    /// engine area, a registry) through its constructor; one it cannot build is
    /// skipped here and falls back to its parameterless constructor in
    /// <see cref="RegisterAll"/>.
    /// </para>
    /// </summary>
    public static int AddHandlersTo(ServiceContainer services, Assembly assembly, ILog log)
    {
        ArgumentNullException.ThrowIfNull(services);
        int added = 0;

        foreach (var type in LoadedAssemblies.Types(assembly, log))
        {
            if (type.GetCustomAttribute<UserMessageAttribute>() is null)
                continue;

            if (type.IsAbstract || type.IsInterface || !typeof(IUserMessage).IsAssignableFrom(type))
                continue;

            if (services.IsRegistered(type))
                continue;

            if (!services.TryCreate(type, out object? handler, out _) || handler is null)
                continue;

            services.AddInstance(type, handler);

            foreach (var contract in type.GetInterfaces())
            {
                if (contract.IsGenericType
                    && contract.GetGenericTypeDefinition() == typeof(IUserMessageEvent<>)
                    && !services.IsRegistered(contract))
                    services.AddInstance(contract, handler);
            }

            added++;
        }

        return added;
    }

    /// <summary>Every message received at least once, with its count, in id order.</summary>
    public IReadOnlyList<(string Name, long Count)> ReceivedCounts()
        => _byIndex.Where(d => d is { Received: > 0 }).Select(d => (d!.Name, d.Received)).ToArray();

    public void Dispatch(int index, ReadOnlySpan<byte> payload)
    {
        if (index < 0 || index >= _byIndex.Count)
        {
            _log.Warning($"[SourceSharp] user message index {index} out of range (have {_byIndex.Count})\n");
            return;
        }

        _byIndex[index]?.NoteReceived();

        // A declaration with no handler is not an error and not a warning: it
        // is the normal case for a message a native HUD element reads. The
        // native hook has already had its own copy of these same bytes.
        var handler = _byIndex[index]?.Handler;
        if (handler is null)
            return;

        var reader = new UserMessageReader(payload);
        if (!handler.OnReceived(reader))
            _log.Warning($"[SourceSharp] user message '{_byIndex[index]!.Name}' ended mid-member or was malformed; not dispatched\n");
    }
}

/// <summary>
/// Builds a user message payload, byte for byte the way
/// <see cref="UserMessageReader"/> takes it apart.
///
/// <para>
/// The two are deliberately a pair, and writing them as a pair is what keeps the
/// wire format from being described twice. A message's layout lives in exactly
/// one place — the <c>OnReceived</c> that reads it — and a sender writes the
/// same fields in the same order against the mirror image of that API. There is
/// no schema, no generated struct, and nothing to keep in step.
/// </para>
///
/// <para>
/// A ref struct over a caller-supplied span, so building a message allocates
/// nothing: these are sent from entity inputs and can be sent every tick.
/// </para>
/// </summary>
public ref struct UserMessageWriter
{
    private readonly Span<byte> _data;
    private int _pos;

    public UserMessageWriter(Span<byte> into)
    {
        _data = into;
        _pos  = 0;
    }

    /// <summary>Bytes written so far — what to hand to the send call.</summary>
    public readonly int Length => _pos;

    /// <summary>
    /// True once a write has run out of room.
    ///
    /// Checked rather than thrown, matching <see cref="UserMessageReader.Overran"/>:
    /// a half-written message must not be sent, and the caller is the only one
    /// that knows what to do instead. Every write after the first overrun is
    /// dropped, so the buffer is never partly overwritten.
    /// </summary>
    public bool Overran { get; private set; }

    public void WriteByte(byte value)
    {
        if (_pos + 1 > _data.Length) { Overran = true; return; }
        _data[_pos++] = value;
    }

    /// <summary>
    /// Writes a colour as the four bytes the HUD messages carry it in.
    ///
    /// Here rather than at the call site because every message that carries a
    /// colour carries it this way, and getting the order wrong shifts every
    /// field after it.
    /// </summary>
    public void WriteColor(byte r, byte g, byte b, byte a)
    {
        WriteByte(r);
        WriteByte(g);
        WriteByte(b);
        WriteByte(a);
    }

    public void WriteShort(short value)
    {
        if (_pos + 2 > _data.Length) { Overran = true; return; }
        BitConverter.TryWriteBytes(_data.Slice(_pos, 2), value);
        _pos += 2;
    }

    public void WriteInt(int value)
    {
        if (_pos + 4 > _data.Length) { Overran = true; return; }
        BitConverter.TryWriteBytes(_data.Slice(_pos, 4), value);
        _pos += 4;
    }

    public void WriteFloat(float value)
    {
        if (_pos + 4 > _data.Length) { Overran = true; return; }
        BitConverter.TryWriteBytes(_data.Slice(_pos, 4), value);
        _pos += 4;
    }

    /// <summary>
    /// Writes a NUL-terminated string, which is what the reader expects.
    ///
    /// A string that does not fit is not truncated: a message cut off mid-field
    /// leaves the reader consuming whatever follows as if it were text, and the
    /// overrun flag is how the caller finds out instead.
    /// </summary>
    public void WriteString(string value)
    {
        int needed = System.Text.Encoding.UTF8.GetByteCount(value) + 1;

        if (_pos + needed > _data.Length) { Overran = true; return; }

        _pos += System.Text.Encoding.UTF8.GetBytes(value, _data.Slice(_pos));
        _data[_pos++] = 0;
    }
}
