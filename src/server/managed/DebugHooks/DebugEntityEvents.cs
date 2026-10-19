using SourceSharp.Abi;
using Debug.Api.Server.Interfaces;

namespace SourceSharp.Server;

/// <summary>
/// <see cref="IDebugEntityEvents"/> over one registry's
/// <see cref="ScriptedEntityRegistry.OutputFired"/> (group G4; statics to
/// zero: it was the static Output.AnyFired every registry raised). Each
/// subscription is its own handler, removed when it is disposed.
/// </summary>
public sealed class DebugEntityEvents(IEngineEntities entities, ScriptedEntityRegistry registry) : IDebugEntityEvents
{
    private readonly IEngineEntities _entities = entities ?? throw new ArgumentNullException(nameof(entities));
    private readonly ScriptedEntityRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public IDisposable Subscribe(Action<DebugOutputFired> fired)
    {
        ArgumentNullException.ThrowIfNull(fired);

        Action<ScriptedEntity, string, string?> handler = (entity, output, value) =>
            fired(new DebugOutputFired(
                entity.Handle.Value,
                _entities.IsValid(entity.Handle) ? _entities.GetIndex(entity.Handle) : -1,
                entity.ClassName, output, value));

        _registry.OutputFired += handler;
        return new Subscription(_registry, handler);
    }

    private sealed class Subscription(ScriptedEntityRegistry registry, Action<ScriptedEntity, string, string?> handler) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                registry.OutputFired -= handler;
        }
    }
}
