using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The client's user messages, managed-built (plan_side_isolation §2.2a):
/// IBaseClientDLL::DispatchUserMessage forwards (id, bf_read) here once, and the
/// receiver decides the rest. The bf_read is native memory valid only for this
/// call, so it is never stored.
/// </summary>
public sealed unsafe class UserMessagesClientBinding(Crossings crossings, IUserMessageDispatch receiver) : IManagedUserMessagesClient
{
    // The message's payload arrives copied out whole as one BitBlock (native's
    // ss::BitBlockFromEngine): one crossing a message, none back.
    public byte DispatchUserMessage(int a0, void* a1, int a2)
    {
        crossings.Count(CrossingOp.ClientOnUserMessage);
        return a1 != null && a2 >= BitBlock.HeaderBytes && receiver.Dispatch(a0, new ReadOnlySpan<byte>(a1, a2))
            ? (byte)1 : (byte)0;
    }
}
