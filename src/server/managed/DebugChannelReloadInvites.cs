using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// The invitations a freshly built server module owes the clients already in.
///
/// <para>
/// <b>WHY.</b> The server invites a client to the debug channel once, when it
/// becomes active (<c>ServerModule.OnClientActive</c>). <c>cs_sv_reload</c>
/// builds a new module without any client becoming active again, and the old
/// module's dev API took its listener and every session with it; the new
/// listener is on a new port, so a client's carried resume data
/// (<c>DebugChannelResume</c>) is dead too. So the new module's first frame
/// invites every connected slot afresh, and the client takes a fresh
/// invitation over whatever it held.
/// </para>
///
/// <para>
/// ONCE PER MODULE, and a slot the join already invited is skipped. On a fresh
/// boot the first frame comes before any client is in, so the sweep sends
/// nothing. Owned by the module's composition: a reloaded module gets a new,
/// empty one, which is what makes it sweep.
/// </para>
/// </summary>
public sealed class DebugChannelReloadInvites
{
    private readonly HashSet<int> _invited = new();
    private bool _swept;

    /// <summary>An invitation went to <paramref name="slot"/>; the sweep skips it.</summary>
    public void Invited(int slot) => _invited.Add(slot);

    /// <summary>The first call: every connected slot not yet invited, ascending. Every later call: none.</summary>
    /// <param name="connected">Whether a player is in that slot now.</param>
    public IReadOnlyList<int> Due(Func<int, bool> connected)
    {
        if (_swept)
            return Array.Empty<int>();

        _swept = true;

        var due = new List<int>();

        for (int slot = 1; slot <= EngineLimits.MaxPlayerSlots; slot++)
        {
            if (connected(slot) && !_invited.Contains(slot))
                due.Add(slot);
        }

        return due;
    }
}
