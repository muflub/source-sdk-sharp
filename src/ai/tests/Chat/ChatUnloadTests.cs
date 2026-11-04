using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using SourceSharp.Ai.Hl2mp.Chat;
using Xunit;

namespace SourceSharp.Ai.Tests.Chat;

/// <summary>
/// The server's collectible load context still unloads with chat on (plan_hl2mp_ai
/// §3.6): a second load of the AI's assemblies runs the real writer, filter and
/// worker against a loopback server, one request answered and one left in
/// flight, then stops and is unloaded. It must be collected; the serializer would
/// have kept it, as it did the routes' (<c>NoAiRouteCallsTheSerializer</c>).
/// </summary>
public sealed class ChatUnloadTests
{
    private sealed class AiCopyContext() : AssemblyLoadContext("ChatUnload", isCollectible: true)
    {
        private static readonly string[] Copied = ["SourceSharp.Ai", "SourceSharp.Ai.Hl2mp"];

        protected override Assembly? Load(AssemblyName name)
        {
            if (!Copied.Contains(name.Name))
                return null;
            string dir = Path.GetDirectoryName(typeof(OllamaChatWriter).Assembly.Location)!;
            return LoadFromAssemblyPath(Path.Combine(dir, name.Name + ".dll"));
        }
    }

    [Fact]
    public void TheServersContextUnloadsWithARequestInFlight()
    {
        using var answering = new OllamaChatWriterTests.Server(
            OllamaChatWriterTests.Server.Ok("{\"message\":{\"role\":\"assistant\",\"content\":\"nice shot\"},\"done\":true}"));
        using var silent = new OllamaChatWriterTests.Server(null);
        (WeakReference weak, string? line) = RunAndUnload(answering.Url, silent.Url, () => silent.Accepted);
        Assert.Equal("nice shot", line);
        for (int i = 0; i < 20 && weak.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(weak.IsAlive, "the context that ran chat with a request in flight unloads");
    }

    /// <summary>Drives the copy by reflection: its types are not this assembly's.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Weak, string? Line) RunAndUnload(string answering, string silent, Func<int> accepted)
    {
        var context = new AiCopyContext();
        Assembly binding = context.LoadFromAssemblyName(new AssemblyName("SourceSharp.Ai.Hl2mp"));
        Assembly core = context.LoadFromAssemblyName(new AssemblyName("SourceSharp.Ai"));
        Assert.Same(context, AssemblyLoadContext.GetLoadContext(core));
        string? line = Exercise(binding, core, answering, silent, accepted);
        var weak = new WeakReference(context, trackResurrection: true);
        context.Unload();
        return (weak, line);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string? Exercise(Assembly binding, Assembly core, string answering, string silent, Func<int> accepted)
    {
        Type T(string name) => core.GetType("SourceSharp.Ai." + name, true)!;
        object words = T(nameof(ChatWords)).GetMethod(nameof(ChatWords.Parse))!.Invoke(null, [File.ReadAllText(SourceSharp.Tests.RepoLayout.Repo("game", "mod_test", "scripts", "ai", "hl2mp_chat.json"))])!;
        object filter = Activator.CreateInstance(T(nameof(ChatOutputFilter)), words)!;
        object counters = Activator.CreateInstance(T(nameof(ChatCounters)))!;
        object writer = binding.GetType(typeof(OllamaChatWriter).FullName!, true)!.GetMethod(nameof(OllamaChatWriter.Create))!.Invoke(null, null)!;
        object worker = Activator.CreateInstance(T(nameof(ChatWorker)), writer, filter, counters, 4)!;
        worker.GetType().GetMethod(nameof(ChatWorker.Start))!.Invoke(worker, [null]);
        object prompt = Activator.CreateInstance(T(nameof(ChatPrompt)), "you are a player", "say something", 8)!;
        object Job(long id, string url)
        {
            object target = Activator.CreateInstance(T(nameof(ChatTarget)), url, "m")!;
            return Activator.CreateInstance(T(nameof(ChatJob)), id, 2, prompt, target, 30f, "Pia", null, null)!;
        }

        MethodInfo post = worker.GetType().GetMethod(nameof(ChatWorker.TryPost))!;
        MethodInfo take = worker.GetType().GetMethod(nameof(ChatWorker.TryTake))!;
        Assert.True((bool)post.Invoke(worker, [Job(1, answering)])!);
        object?[] args = [null];
        long stop = Environment.TickCount64 + 10000;
        while (!(bool)take.Invoke(worker, args)! && Environment.TickCount64 < stop)
            Thread.Sleep(1);
        string? line = (string?)args[0]?.GetType().GetProperty(nameof(ChatResult.Line))!.GetValue(args[0]);
        Assert.True((bool)post.Invoke(worker, [Job(2, silent)])!);
        while (accepted() == 0 && Environment.TickCount64 < stop)
            Thread.Sleep(1);
        Assert.True(accepted() > 0, "the second request is in flight");
        Assert.True((bool)worker.GetType().GetMethod(nameof(ChatWorker.Stop))!.Invoke(worker, [ChatWorker.JoinMilliseconds])!, "the stop joined");
        ((IDisposable)worker).Dispose();
        ((IDisposable)writer).Dispose();
        return line;
    }
}
