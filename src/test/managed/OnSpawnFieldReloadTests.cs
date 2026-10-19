using System.IO;
using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// A plain managed field that <c>OnSpawn</c> fills must survive a hot reload.
///
/// <para>
/// <b>THE CLASS OF BUG</b> (docs/todo.md, added at main 2b1cd8a5): a reload
/// rebuilds each managed entity WITHOUT running <c>OnSpawn</c>, and carries
/// only what the save tiers describe — <c>[SaveField]</c> members,
/// <c>ISaveCustom</c>, <c>[ScriptVars]</c> key sets and <c>[NetField]</c>
/// state. A plain field written once in <c>OnSpawn</c> rides none of them, so
/// the rebuilt instance holds its constructor default: <c>FuncMoveLinear</c>'s
/// two ends came back as (0,0,0) and the door walked to the world origin
/// (fixed field-by-field in 070933fd; this file is the gate for the class).
/// </para>
///
/// <para>
/// <b>THE SWEEP</b>: every concrete <see cref="ScriptedEntity"/> class in the
/// server assembly is spawned through the scripted registry — the real
/// <c>OnSpawn</c>, over the real field declarations — saved with
/// <c>SaveForReload</c> and rebuilt in a fresh registry with
/// <c>RestoreAfterReload</c>. Any field whose value differs afterwards is a
/// value the reload threw away, and names itself in the failure. A field that
/// is deliberately recomputed after a reload belongs in <see cref="Waivers"/>
/// with a reason, and the reason is not "it worked".
/// </para>
///
/// <para>
/// <b>THE CONTROL</b> (<see cref="EveryMarkedFieldSurvivesTheRoundTrip"/>)
/// puts a sentinel into every <c>[SaveField]</c> member the same classes
/// declare and requires it back. Without it the sweep's green could be the
/// round trip silently carrying nothing at all — see
/// <see cref="SpawnedFieldsComeBackFromTheRoundTrip"/> for what each arm can
/// and cannot see.
/// </para>
/// </summary>
public class OnSpawnFieldReloadTests
{
    /// <summary>
    /// Fields the round trip is ALLOWED to lose, each with the reason it is
    /// recomputed instead of carried. A field enters this list only if it
    /// passes the sentence twice: who recomputes it, and from what.
    /// </summary>
    private static readonly Dictionary<(string ClassName, string Member), string> Waivers = new()
    {
    };

    private static readonly Vector DrawnOrigin = new(-3843f, 3034f, 12.5f);

    /// <summary>
    /// The concrete scripted entity classes, as the registry sees them — minus
    /// the harness's own fixtures, which live in this test assembly's namespace.
    /// <c>test_injection_missing</c> ASKS for a service nobody registers; it is
    /// a refusal contract owned by the injection tests, and sweeping it here
    /// would make the sweep red for a thing that is correct.
    /// </summary>
    private static IReadOnlyList<Type> ConcreteClasses()
        => LoadedAssemblies.Types(typeof(FuncMoveLinear).Assembly, new RecordingLog())
            .Where(t => t.GetCustomAttributes<ScriptedEntityAttribute>().Any()
                        && !t.IsAbstract && !t.IsInterface
                        && typeof(ScriptedEntity).IsAssignableFrom(t)
                        && t.Namespace != "SourceSharp.Tests")
            .ToArray();

    /// <summary>
    /// A registry wired like the server module for the services its entities
    /// ask for beyond the engine areas <see cref="TestServices.Services"/>
    /// carries. A class the harness cannot build is invisible to this sweep —
    /// that is a harness gap, not a class bug — so these registrations are
    /// what makes the sweep cover EVERY managed class, mirroring
    /// <c>ServerModule</c>'s composition over the same double.
    /// </summary>
    private static ScriptedEntityRegistry NewRegistry(out RecordingEngine engine)
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(FuncMoveLinear).Assembly);
        // The sweep spawns every class in the tree; an arbitrary class's
        // OnSpawn reads whatever engine values it likes, and the sweep cares
        // about the FIELDS it wrote, not about which reads it made. A strict
        // double would fail the sweep on the 400th class's PrefetchSound and
        // name the wrong bug. The engine-answer-of-zero is fine here because
        // the round trip is the same registry on both sides.
        var services = Services(out engine);
        engine.Strict = false;
        // Non-zero so a field computed from the spawn origin is not the
        // constructor default wearing a disguise: before != after must be
        // provable, or the sweep arm below cannot fail.
        engine.Returns("Entities.GetSpawnOrigin", DrawnOrigin);

        // The module's composition, over the double (see the summary): the
        // services the thirty refusing classes' constructors ask for. Real
        // objects where the module registers real ones — a refused spawn
        // hides its fields from the sweep entirely, and these answer the
        // zero-engine the same way the doubles do.
        var log = new RecordingLog();
        services.AddInstance<IEntityDirectory>(registry);
        services.AddInstance(new Flames(log, registry, engine));
        services.AddInstance(new Corpses(log, engine, engine, registry));
        services.AddInstance<IPickupDirectory>(
            new Pickups(log, engine, engine, engine, engine));
        services.AddInstance(new TemplateStore(log));
        services.AddInstance<IGameEventFeed>(
            new GameEventFeed(new PlayerConnectEvent(), new PlayerDeathEvent(),
                              new PlayerActivateEvent(), new PlayerTeamEvent(),
                              new PlayerDisconnectEvent()));
        services.AddInstance<IEntityRoster<EnvFire>>(new EntityRoster<EnvFire>());
        services.AddInstance<IEntityRoster<PhysRagdollMagnet>>(new EntityRoster<PhysRagdollMagnet>());
        services.AddInstance<IEntityRoster<EnvPlayerSurfaceTrigger>>(new EntityRoster<EnvPlayerSurfaceTrigger>());
        services.AddInstance<IEntityRoster<MapDecal>>(new EntityRoster<MapDecal>());
        services.AddInstance<IViewControlHolders>(new ViewControlHolders());
        services.AddInstance<IVoteIndices>(new VoteIndices());
        services.AddInstance<ISoundscapes>(new Soundscapes(log, engine, engine, engine, engine, engine));
        services.AddInstance<IPlayerDirectory>(new Players(log, engine, engine, engine, NoButtons));
        services.AddInstance<IPlayerButtons>(NoButtons);
        services.AddInstance(new PvsCheckClient(registry, engine, engine));
        services.AddInstance<ITeFire>(
            new TeFire(log, new TempEntRegistry(log), engine, engine));
        services.AddInstance(new DamageHitGroups());
        services.AddInstance(services.Create<Arsenal>());
        services.AddInstance<IPhysicsConversion>(new NoConversion());

        registry.Services = services;
        return registry;
    }

    private sealed record FieldEntry(MemberInfo Member, Type DeclaringType, FieldInfo Read, object? Before);

    /// <summary>
    /// The carried-tier members of one class: fields and auto-property
    /// backing fields, across the entity levels of the hierarchy only (the
    /// <see cref="ScriptedEntity"/> base's own state is the save tiers'
    /// business and is checked by the reload tests that own it).
    /// </summary>
    private static List<FieldEntry> Snapshot(ScriptedEntity instance, Type type,
                                             Func<FieldInfo, bool> take)
    {
        var entries = new List<FieldEntry>();
        var chain = new List<Type>();
        for (Type? t = type; t is not null && t != typeof(ScriptedEntity); t = t.BaseType)
            chain.Add(t);
        chain.Reverse();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Type level in chain)
        {
            foreach (FieldInfo field in level.GetFields(BindingFlags.Instance | BindingFlags.Public
                                                        | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                // Backing fields are taken once, under their property's name,
                // below -- exactly as SaveFieldCatalog skips them here.
                if (field.Name.Length > 0 && field.Name[0] == '<')
                    continue;
                if (!seen.Add(level.Name + "." + field.Name))
                    continue;
                if (!take(field))
                    continue;
                entries.Add(new FieldEntry(field, level, field, field.GetValue(instance)));
            }

            foreach (PropertyInfo property in level.GetProperties(BindingFlags.Instance | BindingFlags.Public
                                                                   | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (property.GetIndexParameters().Length != 0)
                    continue;
                FieldInfo? backing = level.GetFields(BindingFlags.Instance | BindingFlags.NonPublic
                                                     | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(f => f.Name == $"<{property.Name}>k__BackingField");
                if (backing is null)
                    continue; // an expression-body property: nothing to carry
                if (!seen.Add(level.Name + "." + property.Name))
                    continue;
                if (!take(backing))
                    continue;
                entries.Add(new FieldEntry(property, level, backing, backing.GetValue(instance)));
            }
        }

        return entries;
    }

    /// <summary>
    /// True for the types a reload COULD carry (what SaveMarkedState spells),
    /// and false for the framework's own members, which no save owns. A
    /// <c>readonly</c> field cannot be written by <c>OnSpawn</c> either, so it
    /// cannot be lost by the rebuild.
    /// </summary>
    private static bool Carriable(FieldInfo field)
    {
        if (field.IsInitOnly)
            return false;

        Type t = field.FieldType;
        if (t == typeof(Output))
            return false; // outputs are their own save tier
        return t == typeof(string) || t == typeof(bool) || t == typeof(int) || t == typeof(float)
               || t == typeof(Vector) || t == typeof(QAngle) || t == typeof(EntityHandle)
               || t == typeof(byte) || t == typeof(sbyte) || t == typeof(short) || t == typeof(ushort)
               || t == typeof(uint) || t == typeof(long) || t == typeof(ulong) || t == typeof(double)
               || t.IsEnum;
    }

    /// <summary>
    /// The member, or the property behind a backing field, carries
    /// <c>[SaveField]</c>.
    /// </summary>
    private static bool IsMarked(FieldInfo field)
    {
        if (field.GetCustomAttribute<SaveFieldAttribute>(inherit: false) is not null)
            return true;

        return OwnedProperty(field) is { } property
               && property.GetCustomAttribute<SaveFieldAttribute>(inherit: false) is not null;
    }

    /// <summary>
    /// True when another save tier already owns this member: its declaring
    /// level is a <c>[ScriptVars]</c> map-key set (every property there is a
    /// key), or the property behind a backing field is a <c>[NetField]</c>.
    /// Those are carried by their own tiers, which their own tests gate; a
    /// second carry would double-write, in an order nobody chose.
    /// </summary>
    private static bool IsCarriedByAnotherTier(FieldInfo field)
    {
        if (field.DeclaringType?.GetCustomAttribute<ScriptVarsAttribute>(inherit: false) is not null)
            return true;

        return OwnedProperty(field) is { } property
               && property.GetCustomAttribute<NetFieldAttribute>(inherit: false) is not null;
    }

    /// <summary>The property an auto-property backing field belongs to, or null.</summary>
    private static PropertyInfo? OwnedProperty(FieldInfo field)
    {
        if (field.Name[0] != '<' || field.DeclaringType is not { } level)
            return null;

        int close = field.Name.IndexOf('>');
        if (close < 1)
            return null;

        string propertyName = field.Name[1..close];
        return level.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public
                                              | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
    }

    private static EntityHandle HandleFor(int index)
        => new(0x0004_1000u + (uint)(index * 4));

    /// <summary>
    /// Why the registry refused to build a class: the constructor services its
    /// container does not have. "Invisible to this sweep" is a harness gap, not
    /// a class bug, so the name of the missing service is the actionable half
    /// of the red message — it says exactly what to register.
    /// </summary>
    private static string MissingServices(Type type, ScriptedEntityRegistry registry)
    {
        try
        {
            var services = registry.Services;
            var missing = new List<string>();
            foreach (var dependency in ServiceActivator.For(type).Dependencies)
            {
                if (services?.Resolve(dependency) is null)
                {
                    missing.Add(dependency.Name);
                }
            }
            return missing.Count == 0
                ? "every constructor service resolved; the refusal is something else"
                : "the harness registers no " + string.Join(", ", missing);
        }
        catch (Exception e)
        {
            return $"the refusal reason could not be read: {e.GetType().Name}: {e.Message}";
        }
    }

    /// <summary>
    /// THE SWEEP. Spawn every class, carry, rebuild, and put every plain
    /// field's value back where <c>OnSpawn</c> left it. Red names each lost
    /// field: class, member, value thrown away, value the rebuild holds.
    /// </summary>
    [Fact]
    public void SpawnedFieldsComeBackFromTheRoundTrip()
    {
        var lost = new List<string>();
        int swept = 0;

        int index = 0;
        foreach (Type type in ConcreteClasses())
        {
            index++;
            string className = type.GetCustomAttributes<ScriptedEntityAttribute>().First().ClassName;

            _reload.Clear();
            var registry = NewRegistry(out _);
            var handle = HandleFor(index);
            try
            {
                registry.Spawn(handle, className);
            }
            catch (Exception e)
            {
                lost.Add($"{type.Name}: OnSpawn threw ({e.GetType().Name}: {e.Message}) — the sweep " +
                         $"cannot see this class's fields until the spawn is fixable in-process");
                continue;
            }

            ScriptedEntity? instance = registry.Get(handle);
            if (instance is null)
            {
                lost.Add($"{type.Name}: spawned with no instance — {MissingServices(type, registry)}");
                continue;
            }

            var before = Snapshot(instance!, type, f => Carriable(f) && !IsMarked(f) && !IsCarriedByAnotherTier(f));
            swept += before.Count;

            registry.SaveForReload(_reload);

            var rebuilt = NewRegistry(out _);
            if (rebuilt.RestoreAfterReload(_reload) != 1)
            {
                lost.Add($"{type.Name}: the round trip restored {rebuilt.LastRestored.Count} of 1 — " +
                         $"the record was written but not put back ({rebuilt.LastRestoreFailures} rebuild " +
                         $"failures, first '{rebuilt.LastRestoreFirstFailure}')");
                continue;
            }

            ScriptedEntity? after = rebuilt.Get(handle);
            if (after is null)
            {
                lost.Add($"{type.Name}: restored, but the rebuilt registry cannot resolve its handle");
                continue;
            }

            foreach (FieldEntry entry in before)
            {
                object? now = entry.Read.GetValue(after);
                if (Equals(entry.Before, now))
                    continue;

                if (Waivers.TryGetValue((type.Name, entry.Member.Name), out string? reason))
                    continue;

                // The DECLARING type is named too: the mark goes on the
                // declaration, and a subclass loss is fixed in its base —
                // FuncPlatRot._state says "put it on FuncPlat", once for
                // every class that derives.
                lost.Add($"{type.Name}.{entry.Member.Name} (declared by {entry.DeclaringType.Name}): " +
                         $"OnSpawn left {Describe(entry.Before)} " +
                         $"and the rebuild holds {Describe(now)}. [SaveField] it, or waive it " +
                         $"with who-recomputes-it-and-from-what.");
            }
        }

        Assert.True(swept > 200,
            $"the sweep read only {swept} fields; the walk found almost nothing, " +
            $"which is a broken walk, not a clean world");

        // The full list goes beside the assembly: xUnit truncates an assert
        // message to the first ~40 characters, and the point of this fact is
        // the names in the list.
        File.WriteAllText(LossListPath,
            $"# swept {swept} fields, {lost.Count} lost\n" + string.Join("\n", lost) + "\n");

        Assert.True(lost.Count == 0,
            $"{lost.Count} field(s) lost their OnSpawn value across a reload; " +
            $"{LossListPath} names each one");
    }

    /// <summary>Where the sweep's red writes its full list (see the fact).</summary>
    private static string LossListPath
        => Path.Combine(AppContext.BaseDirectory, "onspawn-reload-losses.txt");

    /// <summary>
    /// THE CONTROL: every <c>[SaveField]</c> member is actually carried. A
    /// sentinel the classes' own code would never produce goes in after the
    /// spawn, and must come back — so a sweep green cannot be the round trip
    /// saving nothing (a typo'd attribute, a catalog that stopped walking, a
    /// SaveForReload that wrote no marked tier).
    /// </summary>
    [Fact]
    public void EveryMarkedFieldSurvivesTheRoundTrip()
    {
        var missing = new List<string>();
        int marked = 0;

        int index = 0;
        foreach (Type type in ConcreteClasses())
        {
            index++;
            string className = type.GetCustomAttributes<ScriptedEntityAttribute>().First().ClassName;

            _reload.Clear();
            var registry = NewRegistry(out _);
            var handle = HandleFor(index);
            try
            {
                registry.Spawn(handle, className);
            }
            catch (Exception)
            {
                continue; // SpawnedFields... owns the spawn failure; here it would only repeat it
            }

            ScriptedEntity? instance = registry.Get(handle);
            if (instance is null)
                continue; // SpawnedFields... owns the build refusal; here it would only repeat it

            var toStamp = Snapshot(instance, type, f => Carriable(f) && IsMarked(f));
            if (toStamp.Count == 0)
                continue;

            foreach (FieldEntry entry in toStamp)
            {
                object? sentinel = Sentinel(entry.Read.FieldType);
                if (sentinel is null)
                    continue;
                marked++;
                entry.Read.SetValue(instance, sentinel);
            }

            registry.SaveForReload(_reload);

            var rebuilt = NewRegistry(out _);
            if (rebuilt.RestoreAfterReload(_reload) != 1)
            {
                missing.Add($"{type.Name}: a marked member was stamped, but the round trip restored " +
                            $"{rebuilt.LastRestored.Count} of 1 ({rebuilt.LastRestoreFailures} rebuild " +
                            $"failures, first '{rebuilt.LastRestoreFirstFailure}')");
                continue;
            }
            ScriptedEntity? after = rebuilt.Get(handle);
            if (after is null)
            {
                missing.Add($"{type.Name}: restored, but the rebuilt registry cannot resolve its handle");
                continue;
            }

            foreach (FieldEntry entry in toStamp)
            {
                object? sentinel = Sentinel(entry.Read.FieldType);
                if (sentinel is null)
                    continue;
                if (!Equals(entry.Read.GetValue(after), sentinel))
                {
                    missing.Add($"{type.Name}.{entry.Member.Name}: marked [SaveField], handed " +
                                $"{Describe(sentinel)}, came back {Describe(entry.Read.GetValue(after))}");
                }
            }
        }

        // Five marked members exist tree-wide today: FuncMoveLinear's three
        // ends/target, TestBarrel's think count, SsRagdoll's keep flag. Four
        // so a world where the ragdoll cannot spawn in-process still passes;
        // three or fewer is the marked walk finding only FuncMoveLinear, i.e.
        // the control checking nothing.
        Assert.True(marked >= 4,
            $"only {marked} marked members were exercised; the tree declares five, so " +
            $"the marked-tier walk is not finding the marks");

        File.WriteAllText(LossListPath + ".marked", string.Join("\n", missing) + "\n");
        Assert.True(missing.Count == 0,
            $"{missing.Count} marked member(s) did not come back; {LossListPath}.marked names each");
    }

    private readonly ReloadStore _reload = new(Side.Server);

    private static string Describe(object? value)
        => value is null ? "null"
           : value is string s ? $"\"{(s.Length > 24 ? s[..24] + "…" : s)}\""
           : value.ToString() ?? "?";


    /// <summary>
    /// A value the class's own <c>OnSpawn</c> would be unlikely to write for
    /// this type, so a carried value cannot be confused with a default.
    /// </summary>
    private static object? Sentinel(Type type)
    {
        if (type == typeof(string)) return "sf-sentinel-7z";
        if (type == typeof(bool)) return true;
        if (type == typeof(float)) return 1234.25f;
        if (type == typeof(double)) return 9876.5;
        if (type == typeof(int)) return 0x5E11E1;
        if (type == typeof(uint)) return 0x5E11E1u;
        if (type == typeof(long)) return 0x5E11E1L;
        if (type == typeof(ulong)) return 0x5E11E1UL;
        if (type == typeof(short)) return (short)0x1E11;
        if (type == typeof(ushort)) return (ushort)0x1E11;
        if (type == typeof(byte)) return (byte)0xA7;
        if (type == typeof(sbyte)) return (sbyte)0x53;
        if (type == typeof(Vector)) return new Vector(1234.25f, -4321.5f, 0.125f);
        if (type == typeof(QAngle)) return new QAngle(12f, 34f, 56f);
        if (type == typeof(EntityHandle)) return new EntityHandle(0x0004_5E11u);
        if (type.IsEnum)
        {
            Array values = Enum.GetValues(type);
            foreach (object? v in values)
            {
                if (!Equals(v, Activator.CreateInstance(type)))
                    return v;
            }
            return null; // a one-value enum: nothing distinguishes carried from default
        }
        return null;
    }

    // phys_convert's conversion: nothing to convert in a field round trip.
    private sealed class NoConversion : IPhysicsConversion
    {
        public EntityHandle Convert(EntityHandle target, EntityHandle swapModelFrom, float massOverride, bool asleep, bool debris)
            => EntityHandle.None;
    }
}
