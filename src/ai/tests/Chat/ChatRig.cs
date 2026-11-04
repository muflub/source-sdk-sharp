using SourceSharp.Tests;

namespace SourceSharp.Ai.Tests.Chat;

/// <summary>What the chat facts share: the committed data file, a writer that answers at once or never, and a world that records what is said.</summary>
internal static class ChatRig
{
    public static ChatWords Words() => ChatWords.Parse(File.ReadAllText(RepoLayout.Repo("game", "mod_test", "scripts", "ai", "hl2mp_chat.json")));

    public static ChatPersona Pia() => new("Pia Bullseye", "Pia", "cocky about your aim");

    public static ChatPersona Tommy() => new("Tommy Camps Alot", "Tommy", "patient and smug about camping");

    public static ChatPersona Ines() => new("Ines Plodder", "Ines", "slow and self-mocking");

    public static ChatFacts Facts(ChatPersona? persona = null, params string[] recent)
        => new(persona ?? Pia(), 3, 2, 2, 4, "Kofi", 5, recent);

    /// <summary>Answers each prompt with <see cref="Answer"/>, or fails or hangs as told, and keeps what it was asked.</summary>
    public sealed class Writer : IBotChatWriter
    {
        private int _inFlight;
        private int _calls;

        /// <summary>Answers a fresh line each time, so the openings' ring and the fight's rules have nothing to say about it.</summary>
        public Func<ChatPrompt, string> Answer { get; set; }

        public Writer() => Answer = _ => $"line{Calls} here";

        public ChatFailure Fails { get; set; }

        /// <summary>Blocks until its cancel fires, as a server that never answers does.</summary>
        public bool Hangs { get; set; }

        public System.Collections.Concurrent.ConcurrentQueue<ChatPrompt> Asked { get; } = new();

        public int InFlight => Volatile.Read(ref _inFlight);

        public int Calls => Volatile.Read(ref _calls);

        public ChatWriterReply Write(ChatPrompt prompt, ChatTarget target, CancellationToken cancel)
        {
            Interlocked.Increment(ref _calls);
            Asked.Enqueue(prompt);
            Interlocked.Increment(ref _inFlight);
            try
            {
                if (Hangs)
                {
                    cancel.WaitHandle.WaitOne();
                    return new ChatWriterReply(null, ChatFailure.Cancelled);
                }

                return Fails != ChatFailure.None ? new ChatWriterReply(null, Fails, "no server") : new ChatWriterReply(Answer(prompt));
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        public ChatWriterReply Warm(ChatTarget target, CancellationToken cancel)
            => Fails != ChatFailure.None ? new ChatWriterReply(null, Fails, "no server") : new ChatWriterReply("");
    }

    public sealed class World : IChatWorld
    {
        public List<(int Slot, string Line)> Said { get; } = new();

        /// <summary>Called inside <see cref="Say"/>, as the say path's hook calls back.</summary>
        public Action<int, string>? OnSay { get; set; }

        public string Leader { get; set; } = "Kofi";

        public ChatScore ScoreOf(int slot) => new(3, 2, 2, 4, Leader, 5);

        public void Say(int slot, string line)
        {
            Said.Add((slot, line));
            OnSay?.Invoke(slot, line);
        }

        /// <summary>The bots that can see the asker, and where.</summary>
        public Dictionary<int, AiVector> Sees { get; } = new();

        public AiVector? Asker { get; set; }

        public List<(int Asker, ChatMark Mark)> Marks { get; } = new();

        public AiVector? AskerSeenBy(int slot, int asker) => Sees.TryGetValue(slot, out AiVector at) ? at : null;

        public AiVector? AskerAt(int asker) => Asker;

        public void Mark(int asker, in ChatMark mark) => Marks.Add((asker, mark));
    }

    /// <summary>A director over a live worker, with no typing delay and every chance at certainty unless a fact says otherwise.</summary>
    public sealed class Room : IDisposable
    {
        public const int A = 2, B = 3, C = 4, Human = 1;

        public Writer Writer { get; } = new();

        public World World { get; } = new();

        public ChatCounters Counters { get; } = new();

        public ChatWorker Worker { get; }

        public ChatDirector Director { get; }

        public float Now { get; set; } = 100f;

        public Room(ChatSettings? settings = null)
        {
            ChatWords words = Words();
            Worker = new ChatWorker(Writer, new ChatOutputFilter(words), Counters);
            Director = new ChatDirector(words, new ChatPromptBuilder(words), Worker, Counters, World, 0x1234)
            {
                Settings = settings ?? new ChatSettings { ReactChance = 1f, MomentChance = 1f, ReplyChance = 0f, ReactionMinSeconds = 0f, ReactionMaxSeconds = 0f, CharsPerSecond = 1e6f, Scaled = false, IdleSeconds = 0f, HumanHoldSeconds = 0f },
                Target = new ChatTarget("http://127.0.0.1:1", "fake"),
            };
            Worker.Start();
            Director.Join(A, Pia());
            Director.Join(B, Tommy());
            Director.Join(C, Ines());
        }

        public int[] AllBots => [A, B, C];

        /// <summary>Ticks, the clock moving by <paramref name="step"/>, until the writer and the worker are idle and nothing more is coming.</summary>
        public void Settle(float step = 0.015f, int ticks = 40)
        {
            for (int i = 0; i < ticks; i++)
            {
                Now += step;
                Director.Tick(Now);
                SpinUntilIdle();
            }
        }

        private void SpinUntilIdle()
        {
            long stop = Environment.TickCount64 + 5000;
            while (Counters.Requests > Counters.Finished && Environment.TickCount64 < stop)
                Thread.Yield();
        }

        public void Dispose() => Worker.Dispose();
    }
}
