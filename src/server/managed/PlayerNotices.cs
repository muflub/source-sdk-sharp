using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Telling the one player who asked for something why the server said no, as stock's
/// <c>ClientPrint</c> tells a player: a line in that player's console and the same
/// words in the centre of their screen. The server's own console line is the
/// caller's to write.
/// </summary>
public interface IPlayerNotices
{
    /// <summary>
    /// The slot of the player whose console command is being run now, or 0 for the
    /// server's own console. A listen server's host typing a server command is its
    /// player: the engine names the host's slot for it (stock's
    /// <c>Cmd_ExecuteCommand</c>), so the host hears refusals as any player does.
    /// </summary>
    int CommandIssuer();

    /// <summary>Tells the player in <paramref name="slot"/> <paramref name="line"/>; nobody for 0.</summary>
    void Refused(int slot, string line);
}

/// <summary><see cref="IPlayerNotices"/> over stock's TextMsg, to the asking player's slot only.</summary>
public sealed class PlayerNotices(IUserMessageSender messages, IEnginePlayers players) : IPlayerNotices
{
    /// <summary>Stock's HUD_PRINTCONSOLE, HUD_PRINTTALK and HUD_PRINTCENTER (shareddefs.h).</summary>
    public const byte PrintConsole = 2, PrintTalk = 3, PrintCenter = 4;

    public int CommandIssuer()
    {
        EntityHandle issuer = players.GetCommandClient();
        return issuer == EntityHandle.None ? 0 : issuer.EntryIndex;
    }

    public void Refused(int slot, string line)
    {
        // Slot 0 is everyone to the sender: the server console's refusal stays on the server.
        if (slot <= 0)
            return;
        string text = line.TrimEnd('\n');
        messages.Send(new TextMsg { Destination = PrintConsole, Text = text, Params = [] }, slot, reliable: true);
        messages.Send(new TextMsg { Destination = PrintCenter, Text = text, Params = [] }, slot, reliable: true);
    }
}
