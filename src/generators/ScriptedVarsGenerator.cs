using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SourceSharp.Generators;

/// <summary>
/// Writes the other half of every <c>[ScriptVars]</c> class: the record of
/// whether and when the map wrote each key, a table the debugging views read
/// without reflecting, and — the reason this exists — the dispatch that turns a
/// key NAME into an assignment.
///
/// <para>
/// WHAT IT REPLACES is a dictionary from key name to <c>FieldInfo</c>, built by
/// reflection when the class was registered and consulted per key per entity.
/// That worked, and it had one property nothing could fix: the mapping was
/// resolved at runtime, so a key name that matched nothing was not an error
/// anywhere. A mistyped key in a map produced an entity that read its default
/// and said nothing about it, which is the hardest kind of map bug to find.
/// </para>
///
/// <para>
/// THE DISPATCH IS AN ORDINARY C# SWITCH on the key. The obstacle to that used
/// to be case: map data is inconsistent about it and C#'s string switch is
/// ordinal, so the dispatch was a switch on key LENGTH with a case-insensitive
/// comparison inside each arm — the compiler's own bucketing, hand-rolled
/// because the language would not do it. Lowering the key once, where it is
/// offered, removes the obstacle instead of working around it: the labels below
/// are lower-case, the switch is the plain one, and the compiler emits its own
/// length bucketing and hash jump from it. Less generated code, and one obvious
/// thing rather than two nested ones.
/// </para>
///
/// <para>
/// The lowering is NOT here, and that is the point of it. An entity holds
/// several sets — its own and its bases' — so folding the key inside each
/// dispatch would fold it two or three times per offer, which is more work than
/// the comparisons the fold was meant to replace. It happens once, in
/// ScriptedEntityRegistry.KeyValue, and every generated method below documents
/// that its key arrives lowered.
/// </para>
///
/// <para>
/// A frozen dictionary was the other candidate and is still the wrong one: a
/// map hands an entity every one of its keys at spawn, in bulk, once — perhaps
/// thirty keys against a set of three to twenty — and never again, so the cost
/// that matters is setup. A dictionary would pay a static constructor and a
/// delegate per key to save what a jump table already does for nothing.
/// </para>
///
/// <para>
/// WHAT COUNTS AS A MAP KEY is membership: every SETTABLE property on a
/// <c>[ScriptVars]</c> class is one, keyed by its own name and defaulting to
/// whatever its own initialiser left it holding. There is nothing to write per
/// key and so nothing to forget; the class is the list. <c>[VarName]</c> says so
/// only where the map's spelling and a readable property name genuinely differ.
/// The opt-out is not being settable — see <see cref="IsKey"/>.
/// </para>
///
/// <para>
/// ENUMS (plan_nettable_generic.md Step 12) went where the note said they would:
/// <see cref="KindOf"/>, <see cref="ParseCall"/>, and a rule beside the
/// diagnostics at the bottom. The rule is that every member must carry an
/// explicit value — a map key is a NUMBER A MAPPER TYPED against an FGD written
/// for stock's numbers, so implicit ordinals mean that inserting or reordering a
/// member silently repoints every existing map at a different value. No build
/// error, no runtime error, just weather that changed. See
/// <see cref="ImplicitEnumValues"/>, and <see cref="EveryMemberIsExplicit"/> for
/// the one case the rule cannot be checked in.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ScriptedVarsGenerator : IIncrementalGenerator
{
    private const string SetAttribute     = "SourceSharp.Abi.ScriptVarsAttribute";
    private const string NameAttribute    = "SourceSharp.Abi.VarNameAttribute";


    private const string EntityAttribute = "SourceSharp.Abi.ScriptedEntityAttribute";

    private const string OutputType = "SourceSharp.Abi.Output";

    private const string VectorType = "SourceSharp.Abi.Vector";
    private const string QAngleType = "SourceSharp.Abi.QAngle";
    private const string ColorType  = "SourceSharp.Abi.Color32";

    /// <summary>One declared key: the property and the name a map uses for it.</summary>
    private sealed record Var(
        string Member,
        string Key,
        string TypeName,
        string Kind);

    /// <summary>One var set and everything needed to write its other half.</summary>
    private sealed record VarSet(
        string? Namespace,
        ImmutableArray<string> Containers,
        string TypeName,
        ImmutableArray<Var> Vars,
        ImmutableArray<Diagnostic> Diagnostics);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var sets = context.SyntaxProvider.ForAttributeWithMetadataName(
            SetAttribute,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, _) => Describe(ctx));

        context.RegisterSourceOutput(sets, static (spc, set) =>
        {
            if (set is null)
                return;

            foreach (var diagnostic in set.Diagnostics)
                spc.ReportDiagnostic(diagnostic);

            // Still emitted with no vars: the class derives from ScriptedVarSet
            // and must implement its two abstract members, and a set that
            // declares nothing yet is a legitimate placeholder.
            string hint = set.Containers.IsEmpty
                ? set.TypeName
                : string.Join(".", set.Containers) + "." + set.TypeName;

            spc.AddSource($"{hint}.ScriptedVars.g.cs",
                          SourceText.From(Emit(set), Encoding.UTF8));
        });

        // The entity side. Nothing is written for it — what it produces is the
        // two things only this level can see: a var set an entity holds without
        // saying so, and one key claimed by two of the sets an entity holds.
        var entities = context.SyntaxProvider.ForAttributeWithMetadataName(
            EntityAttribute,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, _) => Inspect(ctx));

        context.RegisterSourceOutput(entities, static (spc, found) =>
        {
            foreach (var diagnostic in found)
                spc.ReportDiagnostic(diagnostic);
        });
    }

    //-------------------------------------------------------------------------
    // The entity side.
    //-------------------------------------------------------------------------

    /// <summary>
    /// What is wrong with one entity's var sets, if anything.
    ///
    /// <para>
    /// TWO QUESTIONS ONLY THIS LEVEL CAN ANSWER. A var set class knows its own
    /// keys and nothing about who holds it; the entity is where several sets
    /// meet, so it is the only place a key claimed by two of them is visible —
    /// and a map key silently reaching one of two properties is undiagnosable,
    /// which is why it fails the build rather than warning.
    /// </para>
    ///
    /// <para>
    /// It is also the only place that can see a var set an entity holds and
    /// forgot to mark. That is what makes <c>[ScriptVars]</c> safe to require:
    /// the marker is unforgettable because leaving it off does not compile.
    /// </para>
    ///
    /// <para>
    /// TWO COLLISIONS, DELIBERATELY DIFFERENT SEVERITIES. Two sets held by the
    /// SAME class claiming one key is an error: there is no ordering that makes
    /// either of them right, so a map writing it would reach one and nothing
    /// could say which. A derived class's set claiming a key its BASE already
    /// claims is a warning, because there is an intent that reading makes sense
    /// — the derived entity meaning to override — and the runtime resolves it
    /// the way that intent expects, offering the most derived set first.
    /// </para>
    ///
    /// <para>
    /// Both the KEY and the member name are compared up the chain, and the key
    /// is the one that matters: <c>[VarName("movedir")]</c> on a derived
    /// property collides with a base property whose key is also "movedir" even
    /// when the two C# names look nothing alike, and that pair is the one the
    /// dispatch actually decides between. Case-insensitively, because that is
    /// how a map key is matched, and a comparison stricter than the dispatch
    /// would miss exactly the pair that collides.
    /// </para>
    /// </summary>
    private static ImmutableArray<Diagnostic> Inspect(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol entity)
            return ImmutableArray<Diagnostic>.Empty;

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        InspectOutputs(entity, diagnostics);

        // What a MORE DERIVED level has already claimed. Filled a level at a
        // time, walking down, so anything found here belongs to a class nearer
        // the entity than the one being read.
        var byKey = new Dictionary<string, Claim>(StringComparer.OrdinalIgnoreCase);
        var byMember = new Dictionary<string, Claim>(StringComparer.OrdinalIgnoreCase);

        for (var type = entity; type is not null && type.SpecialType != SpecialType.System_Object;
             type = type.BaseType)
        {
            // This class's own claims, kept apart from the inherited ones so the
            // two collisions can be told from one another.
            var levelKeys = new Dictionary<string, Claim>(StringComparer.OrdinalIgnoreCase);
            var levelMembers = new Dictionary<string, Claim>(StringComparer.OrdinalIgnoreCase);

            foreach (var member in type.GetMembers())
            {
                if (member is not IFieldSymbol field || field.IsStatic)
                    continue;

                bool marked = field.GetAttributes().Any(
                    a => a.AttributeClass?.ToDisplayString() == SetAttribute);

                bool isSet = field.Type.GetAttributes().Any(
                    a => a.AttributeClass?.ToDisplayString() == SetAttribute);

                if (!marked)
                {
                    // Held but not declared: the keys would never arrive, and
                    // nothing at runtime could tell that from a map that never
                    // wrote them.
                    if (isSet)
                    {
                        diagnostics.Add(Diagnostic.Create(MissingMarker,
                            field.Locations.FirstOrDefault(), entity.Name, field.Name,
                            field.Type.Name));
                    }

                    continue;
                }

                if (!isSet)
                {
                    diagnostics.Add(Diagnostic.Create(NotAVarSet,
                        field.Locations.FirstOrDefault(), field.Name, field.Type.Name));
                    continue;
                }

                if (field.Type is not INamedTypeSymbol set)
                    continue;

                var where = field.Locations.FirstOrDefault();

                foreach (var (key, property) in KeysOf(set))
                {
                    var claim = new Claim(type.Name, set.Name, property, key, where);

                    if (levelKeys.TryGetValue(key, out var clash))
                    {
                        // Unless it is ONE set claiming the key twice, which is
                        // SSVAR004's to report and says it far better — this
                        // would name the same class as both halves of a clash
                        // between two, which reads like a bug in the check.
                        if (clash.Set != claim.Set)
                        {
                            diagnostics.Add(Diagnostic.Create(SharedKey, where,
                                type.Name, key, clash.Set, claim.Set));
                        }

                        continue;
                    }

                    levelKeys[key] = claim;
                    levelMembers[property] = claim;
                }
            }

            // Now against everything nearer the entity. Reported at the DERIVED
            // site, which is the one a reader can change and the one that wins.
            foreach (var pair in levelKeys)
            {
                if (byKey.TryGetValue(pair.Key, out var derived))
                {
                    diagnostics.Add(Diagnostic.Create(ShadowedKey, derived.Where,
                        derived.Set, derived.Member, derived.Key,
                        pair.Value.Set, pair.Value.Member, pair.Value.Owner, entity.Name));
                }
            }

            foreach (var pair in levelMembers)
            {
                // Only when the KEYS did not already say it: one collision, one
                // message.
                if (byMember.TryGetValue(pair.Key, out var derived)
                    && !string.Equals(derived.Key, pair.Value.Key, StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(Diagnostic.Create(ShadowedMember, derived.Where,
                        derived.Set, pair.Key, derived.Key,
                        pair.Value.Set, pair.Value.Key, pair.Value.Owner, entity.Name));
                }
            }

            foreach (var pair in levelKeys)
            {
                if (!byKey.ContainsKey(pair.Key))
                    byKey[pair.Key] = pair.Value;
            }

            foreach (var pair in levelMembers)
            {
                if (!byMember.ContainsKey(pair.Key))
                    byMember[pair.Key] = pair.Value;
            }
        }

        return diagnostics.ToImmutable();
    }

    /// <summary>
    /// An output whose name is already taken by something the base declares.
    ///
    /// <para>
    /// THIS IS WHAT ATE func_button's OnDamaged OUTPUT. The entity declared
    /// <c>public readonly Output OnDamaged = new()</c>, and
    /// <c>ScriptedEntity</c> declares <c>OnDamaged</c>, the virtual that decides
    /// how much damage an entity takes. A field and a method cannot share a
    /// name: the field won, the entity could never override the damage handler
    /// — the only name that would do it was taken — and the output could never
    /// fire. A map wiring it got nothing, and nothing said so.
    /// </para>
    ///
    /// <para>
    /// THE COMPILER SAYS THIS TOO, as CS0108, and it is not enough. CS0108 is a
    /// warning, its suggested fix is the <c>new</c> keyword, and <c>new</c>
    /// makes the collision permanent while removing the only sign of it — the
    /// exact wrong move, offered by name at the site. It also goes quiet in the
    /// two places it is most needed: a base member that is <c>internal</c> to
    /// another assembly is invisible to it, and a project that ever learns to
    /// pass over CS0108 loses it entirely. An error here cannot be silenced by
    /// the thing that would reintroduce the bug.
    /// </para>
    ///
    /// <para>
    /// The fix costs one word and changes nothing map-facing, which is the other
    /// reason this is an error rather than a warning: the output names itself,
    /// <c>[OutputName("OnDamaged")]</c>, and the field takes any name that is
    /// free. Stock's FGD, and every map already wired against it, is untouched.
    /// </para>
    ///
    /// <para>
    /// Reported per <c>[ScriptedEntity]</c> level. A base that is itself a
    /// <c>[ScriptedEntity]</c> is inspected in its own right, so the walk stops
    /// there rather than reporting the same line twice.
    /// </para>
    /// </summary>
    private static void InspectOutputs(INamedTypeSymbol entity, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        bool own = true;

        for (var type = entity; type is not null && type.SpecialType != SpecialType.System_Object;
             type = type.BaseType, own = false)
        {
            if (!own && type.GetAttributes().Any(
                    a => a.AttributeClass?.ToDisplayString() == EntityAttribute))
            {
                break;
            }

            foreach (var member in type.GetMembers())
            {
                if (member is not IFieldSymbol field || field.IsStatic)
                    continue;

                if (field.Type.ToDisplayString() != OutputType)
                    continue;

                for (var above = type.BaseType;
                     above is not null && above.SpecialType != SpecialType.System_Object;
                     above = above.BaseType)
                {
                    var hidden = above.GetMembers(field.Name).FirstOrDefault(m => !m.IsStatic);

                    if (hidden is null)
                        continue;

                    diagnostics.Add(Diagnostic.Create(OutputHidesMember,
                        field.Locations.FirstOrDefault(),
                        type.Name, field.Name, above.Name, WordFor(hidden)));

                    // One message per output. Saying it again for every level
                    // that happens to declare the name is noise around the one
                    // line that has to change.
                    break;
                }
            }
        }
    }

    /// <summary>What the hidden member is, for a message a reader can act on.</summary>
    private static string WordFor(ISymbol symbol) => symbol.Kind switch
    {
        SymbolKind.Method   => "method",
        SymbolKind.Property => "property",
        SymbolKind.Field    => "field",
        SymbolKind.Event    => "event",
        _                   => "member",
    };

    /// <summary>One declared key, and enough about it to say where it came from.</summary>
    private readonly record struct Claim(
        string Owner, string Set, string Member, string Key, Location? Where);

    /// <summary>
    /// The map keys a set class declares. One rule, read from two places — here
    /// and in <see cref="Describe"/> — so a key that reaches the entity check is
    /// the same key the dispatch will answer to.
    /// </summary>
    private static IEnumerable<(string Key, string Member)> KeysOf(INamedTypeSymbol set)
    {
        foreach (var member in set.GetMembers())
        {
            if (member is not IPropertySymbol property || !IsKey(property))
                continue;

            yield return (KeyOf(property), property.Name);
        }
    }

    /// <summary>
    /// Whether a property IS a map key.
    ///
    /// <para>
    /// MEMBERSHIP IS THE DECLARATION: a settable instance property on a
    /// <c>[ScriptVars]</c> class is a key, named for itself. There is nothing to
    /// write per key and so nothing to forget, and the class is the list.
    /// </para>
    ///
    /// <para>
    /// The opt-out is not being settable. Something computed from the keys — a
    /// convenience over two of them, a unit conversion — is a get-only property
    /// or an ordinary method, and both are left alone. A constant a default is
    /// written in terms of is a field, and fields are not read here at all.
    /// </para>
    /// </summary>
    private static bool IsKey(IPropertySymbol property)
        => !property.IsStatic
           && !property.IsIndexer
           && property.GetMethod is not null
           && property.SetMethod is { IsInitOnly: false };

    /// <summary>The map key a property answers to: its own name unless one is given.</summary>
    private static string KeyOf(IPropertySymbol property)
    {
        var named = Find(property, NameAttribute);

        if (named is not null && named.ConstructorArguments.Length > 0
            && named.ConstructorArguments[0].Value is string spelled && spelled.Length > 0)
        {
            return spelled;
        }

        return property.Name;
    }

    //-------------------------------------------------------------------------
    // Reading the declaration.
    //-------------------------------------------------------------------------

    private static VarSet? Describe(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol type)
            return null;

        var vars = ImmutableArray.CreateBuilder<Var>();
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        // Case-insensitively, because that is how a map key is matched. Two
        // properties differing only in case would bind the same key, and which
        // one won would depend on declaration order — silently.
        var claimed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var member in type.GetMembers())
        {
            if (member is not IPropertySymbol property)
                continue;

            // See IsKey: membership is the declaration, and the opt-out is not
            // being settable.
            if (!IsKey(property))
            {
                // Unless it said otherwise. [VarName] on something a map can
                // never write is a key that looks declared and never arrives,
                // which is the exact failure this design removes.
                if (Find(property, NameAttribute) is not null)
                {
                    diagnostics.Add(Diagnostic.Create(NeedsGetAndSet,
                        property.Locations.FirstOrDefault(), property.Name));
                }

                continue;
            }

            string typeName = property.Type.ToDisplayString();
            string? kind = KindOf(property.Type);

            if (kind is null)
            {
                diagnostics.Add(Diagnostic.Create(UnsupportedType,
                    property.Locations.FirstOrDefault(), property.Name, typeName));
                continue;
            }

            // THE EXPLICIT-VALUE RULE (plan_nettable_generic.md Step 12). Not
            // documented and hoped for: a map key is a number a mapper typed
            // against stock's FGD, so an enum whose members are implicitly
            // numbered repoints every existing map the moment somebody inserts
            // one. The key still binds, the entity still spawns, and the value
            // means something else.
            //
            // The key is still DECLARED when this fires -- the diagnostic is an
            // error, so nothing is emitted anyway, and dropping the var here as
            // well would bury the real message under "no such key".
            if (kind == "Enum" && EveryMemberIsExplicit(property.Type) == false)
            {
                diagnostics.Add(Diagnostic.Create(ImplicitEnumValues,
                    property.Locations.FirstOrDefault(), property.Name, typeName));
            }

            // The property's own name unless one is given. Matching is
            // case-insensitive, so a map's "startdisabled" reaches
            // StartDisabled without either side saying so.
            string key = KeyOf(property);

            if (claimed.TryGetValue(key, out string? already))
            {
                diagnostics.Add(Diagnostic.Create(DuplicateKey,
                    property.Locations.FirstOrDefault(), key, already, property.Name));
                continue;
            }

            claimed[key] = property.Name;

            // NO DEFAULT IS READ HERE, and that is the point of the shape. The
            // default is the property's own initialiser — ordinary C#, so it
            // can be a Vector or a constant or anything else an expression can
            // be, where an attribute argument had to be a compile-time constant
            // and so could never express one at all.
            vars.Add(new Var(property.Name, key, typeName, kind));
        }

        string? ns = type.ContainingNamespace.IsGlobalNamespace
            ? null
            : type.ContainingNamespace.ToDisplayString();

        // A set may be nested — the tests declare several inside the class that
        // checks them — and the generated half has to be nested identically or
        // it is a different type with the same name.
        var containers = ImmutableArray.CreateBuilder<string>();
        for (var outer = type.ContainingType; outer is not null; outer = outer.ContainingType)
            containers.Insert(0, outer.Name);

        return new VarSet(ns, containers.ToImmutable(), type.Name,
                          vars.ToImmutable(), diagnostics.ToImmutable());
    }

    //-------------------------------------------------------------------------
    // The type rules. Deliberately the same set the map parser in
    // ScriptedVarParse handles, and there is no second list: a kind here with no
    // parse there would not compile.
    //-------------------------------------------------------------------------

    private static string? KindOf(ITypeSymbol type)
    {
        // AN ENUM IS ITS OWN KIND (plan_nettable_generic.md Step 12), and the
        // only one whose parse is generic -- see ParseCall. Not folded to Int:
        // the assignment needs the declared type, a name must be accepted as
        // well as a number, and an undefined number has to be refusable, none of
        // which an int parse can do.
        if (type.TypeKind == TypeKind.Enum)
            return "Enum";

        string name = type.ToDisplayString();

        return name switch
        {
            "float"    => "Float",
            "int"      => "Int",
            "bool"     => "Bool",
            "string"   => "String",
            "string?"  => "String",
            VectorType => "Vector",
            QAngleType => "QAngle",
            ColorType  => "Color",
            _ => null,
        };
    }

    /// <summary>
    /// Whether every member of an enum carries an explicit value, and null when
    /// this compilation cannot tell.
    ///
    /// <para>
    /// THE RULE IS SYNTACTIC BECAUSE THE FACT IS. Metadata records what a member
    /// is worth and not whether anybody wrote it down: an enum compiled into
    /// another assembly is <c>Rain = 0, Snow = 1</c> whether the author typed
    /// those numbers or let the compiler count. There is no property of the
    /// symbol that separates the two, and guessing from the shape would flag
    /// every legitimate <c>0, 1, 2</c> — which is most enums, and exactly the
    /// ones stock's FGD numbers produce.
    /// </para>
    ///
    /// <para>
    /// So the check runs where the declaration is, and returns null where it is
    /// not. Null is a real answer and is treated as one: it is not "the rule
    /// passed". Everything this tree declares FOR a map key is written beside
    /// the entity that reads it, so the case that matters is checked; a shared
    /// domain like <c>DamageType</c> lives in another assembly and is taken on
    /// trust, which is recorded in docs/divergences.md rather than left for
    /// somebody to discover.
    /// </para>
    /// </summary>
    private static bool? EveryMemberIsExplicit(ITypeSymbol type)
    {
        bool sawDeclaration = false;

        foreach (var member in type.GetMembers())
        {
            if (member is not IFieldSymbol { IsConst: true } field)
                continue;

            foreach (var reference in field.DeclaringSyntaxReferences)
            {
                sawDeclaration = true;

                if (reference.GetSyntax() is EnumMemberDeclarationSyntax { EqualsValue: null })
                    return false;
            }
        }

        return sawDeclaration ? true : null;
    }

    private static AttributeData? Find(IPropertySymbol property, string attribute)
        => property.GetAttributes().FirstOrDefault(
            a => a.AttributeClass?.ToDisplayString() == attribute);

    /// <summary>
    /// The parse for a kind. Generic for an enum and nothing else, because the
    /// enum is the one kind whose target type is not implied by the kind's name.
    /// </summary>
    private static string ParseCall(Var v)
        => v.Kind == "Enum"
            ? $"ScriptedVarParse.Enum<{v.TypeName}>"
            : "ScriptedVarParse." + v.Kind;

    private static string Quote(string value)
        => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    //-------------------------------------------------------------------------
    // Writing the other half.
    //-------------------------------------------------------------------------

    private static string Emit(VarSet set)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated/> by SourceSharp.Generators.ScriptedVarsGenerator.");
        sb.AppendLine("// Edit the declaration, not this.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using SourceSharp.Abi;");
        sb.AppendLine();

        if (set.Namespace is not null)
        {
            sb.Append("namespace ").Append(set.Namespace).AppendLine(";");
            sb.AppendLine();
        }

        foreach (var container in set.Containers)
            sb.Append("partial class ").Append(container).AppendLine("\n{");

        sb.Append("partial class ").Append(set.TypeName).AppendLine(" : ScriptedVarSet")
          .AppendLine("{");

        // NO STORAGE FOR THE VALUES. The property is an ordinary one the author
        // wrote, and its default is its own initialiser — which is why a Vector
        // default is expressible at all, where an attribute argument had to be a
        // compile-time constant.
        //
        // ONE FIELD PER KEY CARRIES BOTH ANSWERS: IsSet is "has a stamp", not a
        // second flag beside the stamp that something could forget to set. Two
        // records of one fact is exactly the drift a generated half exists to
        // prevent.
        //
        // The stamp is taken by the DISPATCH, not by the property's setter, and
        // that is what IsSet means: the MAP wrote this key. An initialiser is
        // not an assignment and never was; an entity writing its own var later
        // is the entity's business and not something a mapper asked for.
        foreach (var v in set.Vars)
            sb.Append("    private float _").Append(v.Member).AppendLine("SetAt = Never;");

        foreach (var v in set.Vars)
        {
            sb.AppendLine();

            sb.Append("    /// <summary>Whether the map wrote '").Append(v.Key)
              .AppendLine("'. False means the value is still the</summary>");
            sb.AppendLine("    /// <remarks>declared default, which is a different answer from a map that");
            sb.AppendLine("    /// wrote the default value on purpose.</remarks>");
            sb.Append("    public bool ").Append(v.Member).Append("IsSet => _")
              .Append(v.Member).AppendLine("SetAt >= 0.0f;");
            sb.AppendLine();

            sb.Append("    /// <summary>Game time the map wrote '").Append(v.Key)
              .AppendLine("', or ScriptedVarSet.Never.</summary>");
            sb.Append("    public float ").Append(v.Member).Append("LastSetAt => _")
              .Append(v.Member).AppendLine("SetAt;");
        }

        //---------------------------------------------------------------------
        // The descriptor table: what the debugging views read, with a typed
        // accessor per key so nothing reflects to show an entity's state.
        //---------------------------------------------------------------------
        sb.AppendLine();
        sb.AppendLine("    private static readonly ScriptedVarDescriptor[] s_Descriptors =");
        sb.AppendLine("    {");

        foreach (var v in set.Vars)
        {
            sb.Append("        new ScriptedVarDescriptor(").Append(Quote(v.Key))
              .Append(", ").Append(Quote(v.Member))
              .Append(", ").Append(Quote(FriendlyName(v.Kind, v.TypeName))).AppendLine(",");
            sb.Append("            static s => ScriptedVarParse.Format(((")
              .Append(set.TypeName).Append(")s).").Append(v.Member).AppendLine("),");
            sb.Append("            static s => ((").Append(set.TypeName).Append(")s).")
              .Append(v.Member).AppendLine("IsSet,");
            sb.Append("            static s => ((").Append(set.TypeName).Append(")s).")
              .Append(v.Member).AppendLine("LastSetAt,");

            // The round-trippable spelling, which is Format for everything but
            // an enum -- see ScriptedVarDescriptor. Save and Read are two
            // functions on purpose.
            sb.Append("            static s => ScriptedVarParse.Save(((")
              .Append(set.TypeName).Append(")s).").Append(v.Member).AppendLine("),");

            // And the way to put a stamp back where it was. Reachable only from
            // inside this class, which is where the field is -- a hot reload
            // replaying a key would otherwise stamp it with the time of the
            // replay and quietly rewrite the entity's own history.
            sb.Append("            static (s, at) => ((").Append(set.TypeName).Append(")s)._")
              .Append(v.Member).AppendLine("SetAt = at),");
        }

        sb.AppendLine("    };");
        sb.AppendLine();
        sb.AppendLine("    public override IReadOnlyList<ScriptedVarDescriptor> Descriptors => s_Descriptors;");

        //---------------------------------------------------------------------
        // Reset, from a prototype. See ScriptedVarSet.Reset for why a prototype
        // rather than a re-emitted copy of each initialiser.
        //---------------------------------------------------------------------
        sb.AppendLine();
        sb.AppendLine("    /// <summary>One instance built by the ordinary constructor, so every");
        sb.AppendLine("    /// initialiser is captured exactly and written down only once.</summary>");
        sb.Append("    private static readonly ").Append(set.TypeName)
          .AppendLine(" s_Defaults = new();");
        sb.AppendLine();
        sb.AppendLine("    public override void Reset()");
        sb.AppendLine("    {");

        foreach (var v in set.Vars)
        {
            sb.Append("        ").Append(v.Member).Append(" = s_Defaults.")
              .Append(v.Member).AppendLine(";");
            sb.Append("        _").Append(v.Member).AppendLine("SetAt = Never;");
        }

        sb.AppendLine("    }");

        //---------------------------------------------------------------------
        // The dispatch. See the class comment for why it has this shape.
        //---------------------------------------------------------------------
        sb.AppendLine();
        sb.AppendLine("    /// <summary>The map's value for one key.</summary>");
        sb.AppendLine("    /// <param name=\"key\">");
        sb.AppendLine("    /// The map key, ALREADY LOWERED -- the labels below are lower-case. The");
        sb.AppendLine("    /// fold is not this method's job and cannot be forgotten by a caller:");
        sb.AppendLine("    /// this door is not public, and ScriptedVarSet.OnKeyDataUpdate is the one");
        sb.AppendLine("    /// anything outside the registry reaches, which folds first.");
        sb.AppendLine("    /// </param>");

        // Always `protected`, never anything else: the base declares the
        // dispatch plainly protected and lets the registry in through an
        // internal forwarder, precisely so this line does not have to know
        // whether the assembly being compiled can see SourceSharp.Abi's
        // internals. Both kinds exist -- the server's sets cannot, the tests'
        // can -- and C# wants a different modifier for each.
        sb.AppendLine("    protected override bool OnLoweredKey(string key, string value)");
        sb.AppendLine("    {");

        if (set.Vars.IsEmpty)
        {
            sb.AppendLine("        return false;");
        }
        else
        {
            sb.AppendLine("        switch (key)");
            sb.AppendLine("        {");

            foreach (var v in set.Vars)
            {
                // Lowered HERE, at build time, by the same invariant fold the
                // key goes through at run time. Two keys folding to one label
                // cannot happen: SSVAR004 refuses the pair before this runs, and
                // a duplicate case label would not compile even if it did not.
                sb.Append("            case ")
                  .Append(Quote(v.Key.ToLowerInvariant())).AppendLine(":");

                // Braced, so each section has a scope of its own: the arms of
                // one switch share a declaration space and every one of them
                // wants a `parsed`.
                sb.AppendLine("            {");
                sb.Append("                if (").Append(ParseCall(v))
                  .AppendLine("(value, out var parsed))");
                sb.AppendLine("                {");
                sb.Append("                    ").Append(v.Member).AppendLine(" = parsed;");
                sb.Append("                    _").Append(v.Member).AppendLine("SetAt = Now;");
                sb.AppendLine("                    return true;");
                sb.AppendLine("                }");
                sb.AppendLine();
                sb.AppendLine("                // Claimed even though it could not be read: it IS this");
                sb.AppendLine("                // set's key, and saying so keeps the message on the right");
                sb.AppendLine("                // entity and the declared default in place. Named as it was");
                sb.AppendLine("                // DECLARED rather than as it was matched, because this line");
                sb.AppendLine("                // is read by a person.");
                sb.Append("                ReportUnreadable(").Append(Quote(v.Key))
                  .Append(", value, ").Append(Quote(FriendlyName(v.Kind, v.TypeName)))
                  .AppendLine(");");
                sb.AppendLine("                return true;");
                sb.AppendLine("            }");
                sb.AppendLine();
            }

            sb.AppendLine("            default:");
            sb.AppendLine("                return false;");
            sb.AppendLine("        }");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        for (int i = 0; i < set.Containers.Length; i++)
            sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>C# spelling rather than the CLR's, because that is how it was written.</summary>
    private static string FriendlyName(string kind, string typeName)
    {
        // The struct kinds arrive fully qualified from the semantic model, and
        // "SourceSharp.Abi.QAngle" in a debugging view is noise.
        int dot = typeName.LastIndexOf('.');
        return dot < 0 ? typeName.TrimEnd('?') : typeName.Substring(dot + 1).TrimEnd('?');
    }

    //-------------------------------------------------------------------------
    // Diagnostics. Every one of these is a map key that would otherwise never
    // be read, and never say so.
    //-------------------------------------------------------------------------

    private static readonly DiagnosticDescriptor UnsupportedType = new(
        "SSVAR002", "Unsupported map key type",
        "'{0}' is a {1}, which a map value cannot be read as; use float, int, bool, string, Vector or an enum",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// The rule that makes an enum safe as a map key
    /// (plan_nettable_generic.md Step 12).
    ///
    /// <para>
    /// A MAP KEY IS A NUMBER A MAPPER TYPED, taken from an FGD written against
    /// the numbers stock uses. If the members carry implicit ordinals then
    /// inserting one — or moving one, which is a thing people do to tidy a list
    /// — silently repoints every existing map at a different value. There is no
    /// build error, no runtime error, and no symptom but weather that changed,
    /// which is why this is enforced rather than written in a comment.
    /// </para>
    ///
    /// <para>
    /// STEP 11'S NET-STATE CASE DOES NOT WANT THIS RULE, which is exactly why it
    /// belongs here and not there: both sides of a network pair read the same
    /// assembly, so they agree with each other whatever the numbers are. A map
    /// file is the other party there, and it was written years ago against
    /// numbers nobody can change.
    /// </para>
    ///
    /// <para>
    /// An error rather than a warning, because the failure it prevents is silent
    /// and the fix is to type the numbers that are already implied.
    /// </para>
    /// </summary>
    private static readonly DiagnosticDescriptor ImplicitEnumValues = new(
        "SSVAR012", "A map key's enum must number every member explicitly",
        "'{0}' is a {1}, which leaves at least one member implicitly numbered; a map key is a number a mapper typed against stock's FGD, so inserting or reordering a member would silently repoint every existing map -- give every member an explicit value",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// See <see cref="InspectOutputs"/> for why this is an error and why CS0108
    /// is not enough on its own.
    /// </summary>
    private static readonly DiagnosticDescriptor OutputHidesMember = new(
        "SSVAR005", "An output's name is already taken by its base",
        "{0}.{1} is an output named for {2}.{1}, a {3}; the field hides it, so nothing can override or reach it and the output would never fire -- rename the field and keep the map-facing name with [OutputName(\"{1}\")]",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NeedsGetAndSet = new(
        "SSVAR003", "A map key needs a get and a set",
        "'{0}' carries [VarName] but is not settable, so it is not a map key and nothing will ever assign it; give it a get and a set",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// The rule that makes the implicit name safe.
    ///
    /// <para>
    /// A property's own name IS its map key, and matching is case-insensitive
    /// because map data is inconsistent about case — which means two properties
    /// differing only in capitalisation are one key, and which of them a map
    /// reached would depend on declaration order. This build has already paid
    /// for a case-folded collision once, when <c>DT_func_lod</c> and stock's
    /// <c>DT_Func_LOD</c> turned out to be one name to the engine and a client
    /// matched a stock SendTable to a managed RecvTable. So it fails the build
    /// rather than picking a winner.
    /// </para>
    /// </summary>
    private static readonly DiagnosticDescriptor DuplicateKey = new(
        "SSVAR004", "Two properties claim one map key",
        "key '{0}' is claimed by both '{1}' and '{2}'; map keys match case-insensitively, so only one of them would ever be written",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor SharedKey = new(
        "SSVAR007", "Two var sets on one class claim one map key",
        "{0} holds two var sets claiming key '{1}': {2} and {3}; there is no ordering that makes either right, so a map writing it would reach one of them and nothing would say which",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>
    /// The inherited case, and a warning rather than an error because there is
    /// an intent that reads: a derived entity meaning to override its base's
    /// key. The runtime resolves it that way — the most derived set is offered
    /// the key first — so the code does what the shape suggests, and this says
    /// out loud that a choice was made.
    ///
    /// <para>
    /// Suppressible at the one site, which is the other reason it has an id: a
    /// deliberate override says so with a pragma, rather than the whole project
    /// learning to read past the warning.
    /// </para>
    /// </summary>
    private static readonly DiagnosticDescriptor ShadowedKey = new(
        "SSVAR010", "A var set shadows a map key its base declares",
        "{0}.{1} declares key '{2}', which {3}.{4} already declares on {5}; {6} inherits both var sets and the derived key is the one a map reaches",
        "SourceSharp", DiagnosticSeverity.Warning, isEnabledByDefault: true);

    /// <summary>
    /// The same name for two DIFFERENT keys up the chain. Nothing collides at
    /// dispatch — a map reaches both — but a reader looking at
    /// <c>TriggerXVars.Speed</c> and <c>Vars.Speed</c> has no way to tell they
    /// are two settings, which is worth one line of output.
    /// </summary>
    private static readonly DiagnosticDescriptor ShadowedMember = new(
        "SSVAR011", "A var set reuses a property name its base declares",
        "{0}.{1} is key '{2}', and {3}.{1} is key '{4}' on {5}; {6} inherits both, so one name means two different settings",
        "SourceSharp", DiagnosticSeverity.Warning, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingMarker = new(
        "SSVAR008", "A var set an entity holds without declaring",
        "{0}.{1} is a {2} but carries no [ScriptVars], so its map keys would never arrive; mark the field",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NotAVarSet = new(
        "SSVAR009", "[ScriptVars] on something that declares no keys",
        "'{0}' carries [ScriptVars] but is a {1}, which is not a [ScriptVars] class",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);
}
