using SourceSharp.Abi;
using SourceSharp.Host.Sdk;

namespace TF2.Descent.Server;

/// <summary>The service's commands, carried out on the game thread (spec §6).</summary>
[SideService(Side.Server)]
public sealed class CommandService(
    IHostLink link,
    PlayerService players,
    IConnectedClients clients,
    ClientMessages messages,
    IChat chat,
    IServerConsole console,
    ILog log) : IHostCommandHook, IOrderedHook
{
    public int HookOrder => 10;

    public void OnKick(KickCommand command)
    {
        if (players.BySteamId(command.SteamId) is { } entry)
            clients.Kick(entry.EntIndex, command.Reason);
    }

    public void OnSay(SayCommand command) => chat.PrintAll(command.Text);

    public void OnDrain(DrainCommand command)
    {
        players.Draining = true;
        chat.PrintAll($"This server is closing ({command.Reason}). You will be moved shortly.");
        messages.Notice(0, "This server is closing. You will be moved shortly.");
    }

    public void OnShutdown(ShutdownCommand command) => _ = ShutdownAsync(command.Reason);

    private async Task ShutdownAsync(string reason)
    {
        log.Msg($"[TF2Descent] shutdown: {reason}\n");
        foreach (var entry in players.Players.ToArray())
            await players.Leave(entry);

        link.Dispose();
        console.Quit(0);
    }

    /// <summary>
    /// The player's route now points at their new instance: their client
    /// reconnects and lands there. Their character was already released by the
    /// hop, so leaving here sends only PlayerLeft.
    /// </summary>
    public void OnRetry(RetryCommand command)
    {
        if (players.BySteamId(command.SteamId) is not { } entry)
            return;

        clients.Reconnect(entry.EntIndex);
        _ = players.Leave(entry);
    }

    public void OnExec(string commandId, string command) =>
        link.ReplyExec(commandId, console.Execute(command));

    public void OnHopped(HopNotice hop) =>
        log.Msg($"[TF2Descent] {hop.SteamId} released for the hop to {hop.TargetInstance}" +
                (hop.SameProcess ? $" (this process moves to {hop.Map})\n" : "\n"));
}
