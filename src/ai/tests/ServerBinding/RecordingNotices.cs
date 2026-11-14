using SourceSharp.Server;

namespace SourceSharp.Ai.Tests.ServerBinding;

/// <summary>The refusals told to players, by slot; <see cref="Issuer"/> is the slot that typed the command (0, the server console).</summary>
public sealed class RecordingNotices : IPlayerNotices
{
    public readonly List<(int Slot, string Line)> Told = [];

    public int Issuer { get; set; }

    public int CommandIssuer() => Issuer;

    public void Refused(int slot, string line) => Told.Add((slot, line));
}
