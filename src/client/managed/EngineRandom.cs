using SourceSharp.Abi;

namespace SourceSharp.Client;

/// <summary>
/// The engine's uniform random stream as an injectable service (vstdlib's
/// <c>RandomFloat</c>/<c>RandomInt</c>, which stock client code calls as free
/// functions), acquired on first use. Without the engine's stream it answers
/// the bottom of each range, and says so once.
/// </summary>
public sealed class EngineRandom(Func<IUniformRandom?> acquire, ILog log) : IUniformRandom
{
    private IUniformRandom? _stream;
    private bool _warned;

    private IUniformRandom? Stream()
    {
        if (_stream is null)
        {
            _stream = acquire();
            if (_stream is null && !_warned)
            {
                _warned = true;
                log.Warning("[SourceSharp/Client] no engine random stream: random draws answer their minimum\n");
            }
        }
        return _stream;
    }

    public float RandomFloat(float min, float max) => Stream()?.RandomFloat(min, max) ?? min;

    public int RandomInt(int min, int max) => Stream()?.RandomInt(min, max) ?? min;

    public float RandomFloatExp(float min, float max, float exponent) => Stream()?.RandomFloatExp(min, max, exponent) ?? min;
}
