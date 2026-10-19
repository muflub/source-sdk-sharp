extern alias client;
using PredictionErrorRing = client::SourceSharp.Abi.PredictionErrorRing;
using SourceSharp.Abi;
using client::SourceSharp.Client;
using Debug.Api.Client.Interfaces;

namespace SourceSharp.Tests;

/// <summary>The client half of <see cref="TestDebugHooks"/>: the client's real hooks over test doubles.</summary>
internal static partial class TestDebugHooks
{
    /// <summary>A client log that hands each line to the test's own sinks.</summary>
    internal sealed class ClientLog(Action<string> msg, Action<string> warn) : Debug.Api.Client.Interfaces.IDebugClientLog
    {
        public void Msg(string message) => msg(message);

        public void Warning(string message) => warn(message);
    }

    /// <summary>The client's real hooks (P3), with the log going where the test says.</summary>
    public static DebugClientHooks Client(Action<string>? msg = null, Action<string>? warn = null,
                                        PredictionErrorRing? ring = null, Crossings? crossings = null,
                                        BuildInfo? build = null, IDebugClientFrame? frame = null,
                                        IDebugClientEntities? entities = null,
                                        IDebugClientNetTables? netTables = null,
                                        IDebugClientReload? reload = null,
                                        IDebugClientNetGraph? netGraph = null,
                                        IDebugClientAbsences? absences = null,
                                        IDebugClientUi? ui = null,
                                        IDebugClientProxies? proxies = null,
                                        IDebugClientViewCamera? viewCamera = null,
                                        IDebugClientRender? render = null,
                                        client::SourceSharp.Abi.UserMessageRegistry? messages = null,
                                        client::SourceSharp.Abi.GameEventRegistry? events = null,
                                        IDebugClientParticles? particles = null,
                                        IDebugClientInput? input = null)
        => new(new ClientLog(msg ?? (_ => { }), warn ?? (_ => { })), new ClientDebugBuild(build ?? new BuildInfo(), crossings ?? new Crossings(), messages, events),
               new ClientDebugPrediction(ring ?? new PredictionErrorRing(8)),
               frame ?? Unasked<IDebugClientFrame>(), entities ?? Unasked<IDebugClientEntities>(),
               netTables ?? Unasked<IDebugClientNetTables>(), reload ?? Unasked<IDebugClientReload>(),
               netGraph ?? Unasked<IDebugClientNetGraph>(), absences ?? Unasked<IDebugClientAbsences>(),
               ui ?? Unasked<IDebugClientUi>(), proxies ?? Unasked<IDebugClientProxies>(),
               viewCamera ?? Unasked<IDebugClientViewCamera>(),
               render ?? Unasked<IDebugClientRender>(),
               particles ?? Unasked<IDebugClientParticles>(),
               input ?? Unasked<IDebugClientInput>());

    /// <summary>
    /// A hook a test did not hand over: any call fails the test, rather than
    /// answering a default that would read as the client's state.
    /// </summary>
    internal static T Unasked<T>() where T : class => System.Reflection.DispatchProxy.Create<T, Refusing>();

    /// <summary>The proxy behind <see cref="Unasked{T}"/>.</summary>
    public class Refusing : System.Reflection.DispatchProxy
    {
        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
            => throw new InvalidOperationException($"the test handed over no {targetMethod?.DeclaringType?.Name}; " +
                                                   $"{targetMethod?.Name} was called");
    }
}
