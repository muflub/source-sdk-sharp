using System.Reflection;

namespace SourceSharp.Abi;

/// <summary>
/// One registered temp entity: its declaration, the handle native answered with,
/// and every field it can write — its own and its base chain's.
/// </summary>
public sealed class TempEntType
{
    internal TempEntType(TeClass declaration, int handle, IReadOnlyDictionary<string, TeField> fields, ILog log)
    {
        Class  = declaration;
        Handle = handle;
        Fields = fields;
        Log    = log;
    }

    /// <summary>
    /// The registering side's log (statics to zero): what a writer of this
    /// type warns through when a field is missing or the wrong size.
    /// </summary>
    public ILog Log { get; }

    public TeClass Class { get; }

    /// <summary>What native answered, or -1 if it refused or if this side does not send.</summary>
    public int Handle { get; }

    /// <summary>
    /// Every field addressable in this class's blob, by wire name — this class's
    /// own AND its base chain's.
    ///
    /// <para>
    /// The base's fields are in the base's TABLE, but they are in the DERIVED
    /// class's blob: a nested datatable prop at offset zero means the base's
    /// props read at their own offsets out of the same object the derived ones
    /// do. So a writer for CTESparks has to be able to set m_vecOrigin, which
    /// DT_TESparks does not declare.
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<string, TeField> Fields { get; }

    public int BlobBytes => Class.BlobBytes;

    public bool Registered => Handle >= 0;
}

/// <summary>
/// Every temp entity declared in the shared assembly, discovered by attribute,
/// resolved base-first, and published to native.
///
/// <para>
/// THERE IS NO LIST FILE. Discovery is by <see cref="TempEntityAttribute"/>,
/// exactly as <c>[ScriptedEntity]</c> and <c>[UserMessage]</c> are found,
/// because a shared list is forty-three lanes editing one line and a guaranteed
/// conflict.
/// </para>
///
/// <para>
/// BASE FIRST, and that ordering is load-bearing rather than tidy. A derived
/// class's blob has to start where its base's ends, and its native table has to
/// nest a table that already exists — so the base's blob size must be known
/// before the derived one can be built, and the base must have been registered
/// before the derived one is offered. Discovery order is declaration order,
/// which says nothing about inheritance, so the chain is resolved here.
/// </para>
/// </summary>
public sealed class TempEntRegistry
{
    /// <param name="log">Where this side's registrations are reported (statics to zero).</param>
    public TempEntRegistry(ILog log) => _log = log ?? throw new ArgumentNullException(nameof(log));

    private readonly ILog _log;

    private readonly Dictionary<string, TempEntType> _byClassName = new(StringComparer.Ordinal);
    private readonly Dictionary<int, TempEntType> _byHandle = new();
    private readonly List<TempEntType> _inOrder = new();

    public IReadOnlyList<TempEntType> All => _inOrder;
    public int Count => _inOrder.Count;

    public TempEntType? Find(string className)
        => _byClassName.TryGetValue(className, out var type) ? type : null;

    /// <summary>
    /// The class native answered this handle for, or null.
    ///
    /// <para>
    /// THE CLIENT'S OWN QUESTION. An arriving temp entity carries nothing but
    /// the handle its own side issued — no name, no class id this assembly has
    /// ever seen — so this is how a received blob becomes a declaration. The
    /// server never needs it: it starts from the name it wanted to send.
    /// </para>
    ///
    /// <para>
    /// KEYED BY THE HANDLE AND NOT BY POSITION IN <see cref="All"/>. They are
    /// the same number today because native issues handles in the order it
    /// accepts them and this list is appended in the order it offers them — and
    /// they stop being the same number the moment native REFUSES one, which is
    /// the ordinary state of a staged port. An index into a list that skipped a
    /// refusal plays the wrong effect and nothing anywhere says so.
    /// </para>
    /// </summary>
    public TempEntType? FindByHandle(int handle)
        => _byHandle.TryGetValue(handle, out var type) ? type : null;

    /// <summary>
    /// Discovers, resolves and registers every declaration this side's scan
    /// finds from <paramref name="shared"/>, the side's game assembly (the shared
    /// source compiles into it, plan_side_isolation P7). Returns how many native
    /// accepted.
    ///
    /// <para>
    /// A declaration whose base is not declared here is NOT an error: for the
    /// length of this port a managed table's base is routinely still stock's,
    /// and native resolves it by name from the live class list. What that costs
    /// is the base's blob size, which this side cannot know — so such a class's
    /// own fields start at offset zero and the base's props address whatever
    /// stock put in front of them. Refused loudly rather than registered wrong.
    /// </para>
    /// </summary>
    public int RegisterAll(string side, Assembly shared, IEngineEffects engine)
    {
        var types = new List<Type>();

        foreach (var type in LoadedAssemblies.Types(shared, _log))
        {
            if (type.GetCustomAttribute<TempEntityAttribute>() is null)
                continue;

            types.Add(type);
        }

        return Register(side, types, engine);
    }

    /// <summary>
    /// Registers exactly <paramref name="types"/>, in key order whatever order
    /// they arrive in: what <see cref="RegisterAll"/> does after its scan.
    /// </summary>
    internal int Register(string side, IEnumerable<Type> types, IEngineEffects engine)
    {
        var declared = new List<(TempEntityAttribute Attr, Type Type)>();

        foreach (var type in types)
        {
            if (type.GetCustomAttribute<TempEntityAttribute>() is { } attr)
                declared.Add((attr, type));
        }

        // KEY ORDER (plan_side_isolation §2.1): registration order decides
        // which classes a full native table refuses, so it comes from the
        // classname alone and never from the assembly a declaration sits in.
        declared.Sort((a, b) => string.CompareOrdinal(TempEntityAttribute.ClassNameOf(a.Type),
                                                      TempEntityAttribute.ClassNameOf(b.Type)));

        var byTable = declared.ToDictionary(d => TempEntityAttribute.TableNameOf(d.Type), d => d,
                                            StringComparer.Ordinal);
        var built   = new Dictionary<string, TeClass>(StringComparer.Ordinal);
        var pending = new List<(TempEntityAttribute Attr, Type Type)>(declared);

        int accepted = 0;
        int refused  = 0;

        // Base before derived. A pass that places nothing means what is left has
        // a base this side does not declare, which is either stock's (fine, and
        // handled below) or a cycle (not fine, and named).
        while (pending.Count > 0)
        {
            int placedThisPass = 0;

            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var (attr, type) = pending[i];

                // NOT attr.BaseTable ALONE, and this is the whole of a bug that
                // logs nothing. A converted declaration drops BaseTable and lets
                // the C# base class supply it (docs/te-declaration-conversion.md),
                // so the attribute answers null — which this block read as
                // NOBASE and laid the blob out from offset zero. Meanwhile
                // TeClassBuilder.Build resolves the base from the inheritance
                // and still NESTS it, so every derived field sits exactly where
                // the base's props read. It compiles, it registers, it sends,
                // and nothing anywhere says so. Found by lane RP.
                //
                // It is invisible while the base is DT_BaseTempEntity, which has
                // no fields and no bytes; DT_TEParticleSystem's twelve are what
                // it costs.
                string? baseTable = attr.BaseTable ?? TeClassBuilder.BaseTableOf(type);

                int baseBytes;

                if (baseTable is null)
                {
                    baseBytes = 0;
                }
                else if (built.TryGetValue(baseTable, out var baseClass))
                {
                    baseBytes = baseClass.BlobBytes;
                }
                else if (byTable.ContainsKey(baseTable))
                {
                    continue;   // declared here, not built yet — next pass
                }
                else
                {
                    // A base still owned by stock. Its props read at offsets
                    // this side never computed, so a blob laid out from zero
                    // would put our first field where its first prop reads.
                    _log.Warning(
                        $"[SourceSharp/{side}] temp entity '{TempEntityAttribute.ClassNameOf(type)}' declares base " +
                        $"table '{baseTable}', which no managed declaration owns. Its " +
                        "base props address offsets this side cannot compute, so it is NOT " +
                        "registered — declare the base as a [TempEntity] with " +
                        "RegistersClass = false.\n");

                    pending.RemoveAt(i);
                    refused++;
                    continue;
                }

                // AND ITS NESTED TABLES, for the same reason as its base and
                // one step further out. A table nested as a FIELD —
                // DT_TEEffectDispatch's m_EffectData, the inventory's only one
                // — is a SendPropDataTable native has to point at something for,
                // so the nested description has to be registered FIRST. Base
                // order alone does not give that: DT_EffectData is nobody's
                // base.
                bool nestedReady = true;

                foreach (string nested in NestedTablesOf(type))
                {
                    if (built.ContainsKey(nested))
                        continue;

                    if (byTable.ContainsKey(nested))
                    {
                        nestedReady = false;   // declared here, not built yet — next pass
                        break;
                    }

                    _log.Warning(
                        $"[SourceSharp/{side}] temp entity '{TempEntityAttribute.ClassNameOf(type)}' nests table " +
                        $"'{nested}' as a field, and no managed declaration owns it. Its props " +
                        "would read at offsets this side cannot compute, so it is NOT " +
                        "registered — declare the nested table as a [TempEntity] with " +
                        "RegistersClass = false.\n");

                    pending.RemoveAt(i);
                    refused++;
                    nestedReady = false;
                    break;
                }

                if (!nestedReady)
                    continue;

                TeClass built1 = TeClassBuilder.Build(type, baseBytes);
                built[TempEntityAttribute.TableNameOf(type)] = built1;
                pending.RemoveAt(i);
                placedThisPass++;

                if (Publish(side, built1, built, engine))
                    accepted++;
                else
                    refused++;
            }

            if (placedThisPass == 0)
                break;
        }

        foreach (var (attr, type) in pending)
        {
            _log.Warning(
                $"[SourceSharp/{side}] temp entity '{TempEntityAttribute.ClassNameOf(type)}' was never placed: its " +
                $"base '{attr.BaseTable ?? TeClassBuilder.BaseTableOf(type)}' or a table it nests " +
                $"({string.Join(", ", NestedTablesOf(type))}) is declared here but forms a " +
                "cycle.\n");
            refused++;
        }

        _log.Msg($"[SourceSharp/{side}] {accepted} temp entity table(s) registered" +
                (refused > 0 ? $", {refused} refused" : "") + "\n");

        return accepted;
    }

    private bool Publish(string side, TeClass declaration,
                         Dictionary<string, TeClass> built, IEngineEffects engine)
    {
        int handle = engine.TempEntRegister(declaration);

        // Every field this class's blob holds: the base chain's first, in the
        // order the wire has them, then its own.
        var fields = new Dictionary<string, TeField>(StringComparer.Ordinal);
        var chain  = new List<TeClass>();

        for (TeClass? at = declaration; at is not null; )
        {
            chain.Add(at);
            at = at.BaseTable is not null && built.TryGetValue(at.BaseTable, out var next)
                ? next : null;

            if (chain.Count > 16)
                break;
        }

        for (int i = chain.Count - 1; i >= 0; i--)
            AddFieldsOf(chain[i], 0, fields, built, 0);

        var type = new TempEntType(declaration, handle, fields, _log);

        _byClassName[declaration.ClassName] = type;
        _inOrder.Add(type);

        // -1 is "this side does not send/receive this one", which several
        // classes legitimately are, and it is not a key.
        if (handle >= 0)
            _byHandle[handle] = type;

        if (handle < 0)
        {
            _log.Warning(
                $"[SourceSharp/{side}] temp entity '{declaration.ClassName}' was refused by " +
                "native; the console line above says why. Nothing will fire it.\n");
            return false;
        }

        return true;
    }

    /// <summary>
    /// One class's fields into the writer's map, and everything it NESTS with
    /// them, shifted by where the nesting put it.
    ///
    /// <para>
    /// A base contributes at shift zero, which is why the loop above passes
    /// zero: <c>SendProxy_DataTableToDataTable</c> moves the object pointer by
    /// the datatable prop's offset and stock declares a base's as zero, so the
    /// base's props read at their own offsets out of the same blob. A table
    /// nested as a FIELD is the same mechanism with a non-zero offset, so its
    /// fields are addressable too — at their own offset PLUS the prop's.
    /// </para>
    ///
    /// <para>
    /// Without this, a writer for <c>CTEEffectDispatch</c> could set nothing at
    /// all: <c>DT_TEEffectDispatch</c> declares exactly one prop and it is the
    /// nested table, so every value the effect carries lives one level down.
    /// </para>
    /// </summary>
    private static void AddFieldsOf(TeClass declaration, int shift,
                                    Dictionary<string, TeField> into,
                                    Dictionary<string, TeClass> built, int depth)
    {
        if (depth > 8)
            return;

        foreach (TeField f in declaration.Fields)
        {
            TeField placed = shift == 0 ? f : f with { Offset = f.Offset + shift };

            into[placed.Name] = placed;

            if (f.Kind != TePropKind.DataTable || f.SubTable is null)
                continue;

            if (built.TryGetValue(f.SubTable, out TeClass? nested))
                AddFieldsOf(nested, placed.Offset, into, built, depth + 1);
        }
    }

    /// <summary>
    /// The tables a declaration nests as FIELDS, by name — not its base, which
    /// <see cref="TempEntityAttribute.BaseTable"/> carries and which is a
    /// different shape on the wire.
    /// </summary>
    private static IEnumerable<string> NestedTablesOf(Type type)
    {
        const BindingFlags Where = BindingFlags.Public | BindingFlags.Instance;

        IEnumerable<TePropAttribute?> props =
            type.GetFields(Where).Select(f => f.GetCustomAttribute<TePropAttribute>())
                .Concat(type.GetProperties(Where).Select(p => p.GetCustomAttribute<TePropAttribute>()));

        foreach (TePropAttribute? prop in props)
        {
            if (prop is { Kind: TePropKind.DataTable, SubTable: { Length: > 0 } table })
                yield return table;
        }
    }
}
