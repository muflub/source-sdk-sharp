using SourceSharp.Shared;

namespace SourceSharp.Tests;

/// <summary>
/// A command line a test sets, counting how often it is read: the server's
/// reader since its engine slots were retired (plan_engineapi_vtables).
/// </summary>
internal sealed class RecordingCommandLine : ILocalCommandLine
{
    private IReadOnlyList<string> _args;

    public RecordingCommandLine(params string[] args) => _args = args;

    public int Reads { get; private set; }

    public IReadOnlyList<string> Args
    {
        get
        {
            Reads++;
            return _args;
        }
    }

    public void Set(params string[] args) => _args = args;
}
