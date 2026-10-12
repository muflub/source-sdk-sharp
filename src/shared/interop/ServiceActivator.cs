using System.Reflection;

namespace SourceSharp.Abi;

/// <summary>
/// Marks the constructor the container should use, when a class has more than
/// one and the widest is not the one meant for injection.
///
/// <para>
/// Rarely needed. A class with a single constructor needs nothing, and a class
/// with several is unambiguous as long as only one of them is the widest. This
/// exists for the case where a class keeps a convenience constructor with the
/// same arity as its real one — which is otherwise a build that resolves
/// whichever the reflection layer happened to list first, and that is not a
/// thing to leave to chance.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Constructor, Inherited = false)]
public sealed class InjectAttribute : Attribute
{
}

/// <summary>
/// <summary>
/// Resolves a constructor parameter from the container's NAME table rather than
/// by its type — which is how a class asks for a particular convar or console
/// command.
///
/// <para>
/// Type is the better key wherever there is one, and for a convar declared in
/// this mod there is: <c>SpawnHealthConVar</c> is a class, so asking for it by
/// type is checked by the compiler and cannot be misspelled. This is for the
/// other case — a convar reached by the name a player types, where the only key
/// there has ever been is the string.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
public sealed class NamedAttribute : Attribute
{
    public NamedAttribute(string name)
    {
        Name = name;
    }

    /// <summary>The console name, e.g. "sv_spawn_health" or "ss_end_round".</summary>
    public string Name { get; }
}

/// <summary>
/// Why a type could not be built. Carries the whole chain rather than only the
/// missing service, because the missing one is rarely the interesting part: an
/// entity asking for a rules object that asks for a registry nobody registered
/// reads as "the entity is broken" unless the message says which step failed.
/// </summary>
public sealed class ServiceResolutionException : Exception
{
    public ServiceResolutionException(string message) : base(message)
    {
    }
}

/// <summary>
/// One type's constructor, worked out once and then reused.
///
/// <para>
/// Worked out ONCE is the whole point. Entity instances are created per map
/// entity — a busy map makes hundreds — and every one of them would otherwise
/// re-list the constructors, re-read the parameter attributes and re-decide the
/// same thing. None of that can change after load, so it is decided the first
/// time a type is built and the answer kept.
/// </para>
/// </summary>
public sealed class ServiceActivator
{
    /// <summary>How one parameter is filled.</summary>
    private enum Source
    {
        /// <summary>By service type.</summary>
        Type,

        /// <summary>By console name, from a <see cref="NamedAttribute"/>.</summary>
        Name,

        /// <summary>The container itself.</summary>
        Locator,
    }

    private readonly record struct Parameter(
        Source  Source,
        Type    ServiceType,
        string  Name,
        bool    HasDefault,
        object? DefaultValue,
        string  ParameterName);

    private readonly ConstructorInfo _constructor;
    private readonly Parameter[] _parameters;

    /// <summary>The type this builds.</summary>
    public Type Type { get; }

    /// <summary>
    /// Whether this takes nothing at all — the ordinary case, and worth knowing
    /// because it is the one where the container adds no behaviour and a caller
    /// with no container can still build the type.
    /// </summary>
    public bool IsParameterless => _parameters.Length == 0;

    /// <summary>The service types this asks for, in constructor order.</summary>
    public IEnumerable<Type> Dependencies
        => _parameters.Where(p => p.Source == Source.Type).Select(p => p.ServiceType);

    private ServiceActivator(Type type, ConstructorInfo constructor, Parameter[] parameters)
    {
        Type = type;
        _constructor = constructor;
        _parameters = parameters;
    }

    /// <summary>
    /// Reads a type's constructors and picks the one to use.
    ///
    /// <para>
    /// The rule, in order: a constructor marked <see cref="InjectAttribute"/>;
    /// otherwise the only public one; otherwise the widest public one. Two
    /// equally wide constructors with no <c>[Inject]</c> between them is an
    /// error rather than a coin toss — see the attribute's own remarks.
    /// </para>
    /// </summary>
    /// <exception cref="ServiceResolutionException">
    /// The type has no usable public constructor, or two that are equally good.
    /// </exception>
    public static ServiceActivator For(Type type)
    {
        if (type.IsAbstract || type.IsInterface)
            throw new ServiceResolutionException(
                $"{type.Name} is abstract or an interface; the container builds concrete types only");

        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        if (constructors.Length == 0)
            throw new ServiceResolutionException(
                $"{type.Name} has no public constructor");

        var marked = constructors.Where(c => c.GetCustomAttribute<InjectAttribute>() is not null).ToArray();

        if (marked.Length > 1)
            throw new ServiceResolutionException(
                $"{type.Name} marks {marked.Length} constructors with [Inject]; mark exactly one");

        ConstructorInfo chosen;

        if (marked.Length == 1)
        {
            chosen = marked[0];
        }
        else if (constructors.Length == 1)
        {
            chosen = constructors[0];
        }
        else
        {
            int widest = constructors.Max(c => c.GetParameters().Length);
            var tied = constructors.Where(c => c.GetParameters().Length == widest).ToArray();

            if (tied.Length > 1)
                throw new ServiceResolutionException(
                    $"{type.Name} has {tied.Length} public constructors taking {widest} argument(s) " +
                    $"and none marked [Inject]; the container will not guess between them");

            chosen = tied[0];
        }

        var declared = chosen.GetParameters();
        var parameters = new Parameter[declared.Length];

        for (int i = 0; i < declared.Length; i++)
        {
            var parameter = declared[i];
            var named = parameter.GetCustomAttribute<NamedAttribute>();

            Source source =
                named is not null                                  ? Source.Name :
                parameter.ParameterType == typeof(IServiceLocator) ? Source.Locator :
                                                                     Source.Type;

            parameters[i] = new Parameter(
                source,
                parameter.ParameterType,
                named?.Name ?? string.Empty,
                parameter.HasDefaultValue,
                parameter.HasDefaultValue ? parameter.DefaultValue : null,
                parameter.Name ?? $"#{i}");
        }

        return new ServiceActivator(type, chosen, parameters);
    }

    /// <summary>
    /// Builds one instance, filling each parameter from <paramref name="services"/>.
    ///
    /// <para>
    /// A parameter nothing satisfies falls back to its declared default, and
    /// only fails when there is none. That fallback is deliberate and it is
    /// spelled <c>= null</c> at the declaration, where a reader can see it: an
    /// entity that works without the rules object says so in its own signature,
    /// rather than the container quietly deciding on its behalf that a missing
    /// service was fine.
    /// </para>
    /// </summary>
    /// <exception cref="ServiceResolutionException">A parameter could not be filled.</exception>
    public object Create(IServiceLocator services)
    {
        if (_parameters.Length == 0)
            return _constructor.Invoke(Array.Empty<object?>());

        var arguments = new object?[_parameters.Length];

        for (int i = 0; i < _parameters.Length; i++)
        {
            var parameter = _parameters[i];

            object? value = parameter.Source switch
            {
                Source.Locator => services,
                Source.Name    => services.ResolveNamed(parameter.Name),
                _              => services.Resolve(parameter.ServiceType),
            };

            if (value is null)
            {
                if (parameter.HasDefault)
                {
                    arguments[i] = parameter.DefaultValue;
                    continue;
                }

                string wanted = parameter.Source == Source.Name
                    ? $"nothing is registered under the name '{parameter.Name}'"
                    : $"no service is registered for {parameter.ServiceType.Name}";

                throw new ServiceResolutionException(
                    $"{Type.Name} cannot be constructed: parameter " +
                    $"'{parameter.ParameterName}' cannot be filled because {wanted}");
            }

            // A named lookup is the one that can come back holding the wrong
            // thing: the name table is keyed by a string, so asking for
            // "sv_gravity" as an IConCommand is a question type checking never
            // saw. Caught here rather than left to the constructor call, where
            // it surfaces as a TargetInvocationException naming nothing.
            if (!parameter.ServiceType.IsInstanceOfType(value))
                throw new ServiceResolutionException(
                    $"{Type.Name} asked for '{parameter.Name}' as {parameter.ServiceType.Name}, " +
                    $"but what is registered under that name is a {value.GetType().Name}");

            arguments[i] = value;
        }

        return _constructor.Invoke(arguments);
    }
}
