using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Angles agrees with mathlib bit for bit. Each row is a direction, then the
/// pitch and yaw mathlib's VectorAngles gives it, then the right and up its
/// AngleVectors gives those, recorded from an unoptimised libmathlib.a
/// (2026-10-26): every one of 200,000 such rows matched. The client's native
/// pane split computes the same two vectors, so a server that rounded
/// differently would put a hit in a pane the client draws elsewhere.
/// </summary>
public class AnglesMathlibBitsTests
{
    [Theory]
    [InlineData(0x3f800000u, 0x00000000u, 0x00000000u, 0x80000000u, 0x00000000u, 0x00000000u, 0xbf800000u, 0x80000000u, 0x00000000u, 0x80000000u, 0x3f800000u)]
    [InlineData(0xbf800000u, 0x00000000u, 0x00000000u, 0x80000000u, 0x43340000u, 0xb3bbbd2eu, 0x3f800000u, 0x80000000u, 0x00000000u, 0x00000000u, 0x3f800000u)]
    [InlineData(0x00000000u, 0x3f800000u, 0x00000000u, 0x80000000u, 0x42b40000u, 0x3f800000u, 0x333bbd2eu, 0x80000000u, 0x00000000u, 0x00000000u, 0x3f800000u)]
    [InlineData(0x00000000u, 0xbf800000u, 0x00000000u, 0x80000000u, 0x43870000u, 0xbf800000u, 0xb24cde2eu, 0x80000000u, 0x80000000u, 0x00000000u, 0x3f800000u)]
    [InlineData(0x00000000u, 0x00000000u, 0x3f800000u, 0x43870000u, 0x00000000u, 0x00000000u, 0xbf800000u, 0x80000000u, 0xbf800000u, 0x80000000u, 0x324cde2eu)]
    [InlineData(0x00000000u, 0x00000000u, 0xbf800000u, 0x42b40000u, 0x00000000u, 0x00000000u, 0xbf800000u, 0x00000000u, 0x3f800000u, 0x00000000u, 0xb33bbd2eu)]
    [InlineData(0xbe78ce2du, 0xbf1ecd7du, 0xbed0980bu, 0x41fb93cbu, 0x43789c38u, 0xbf6e5df1u, 0x3ebabb5au, 0x80000000u, 0xbe42d72cu, 0xbef8b7c4u, 0x3f5a662eu)]
    [InlineData(0x00000000u, 0xbf44240bu, 0x3f64218eu, 0x439b5811u, 0x43870000u, 0xbf800000u, 0xb24cde2eu, 0x80000000u, 0xb21b5867u, 0x3f421e08u, 0x3f26e586u)]
    [InlineData(0x3db6640au, 0x3f553d86u, 0xbea0d34au, 0x41a46edau, 0x42a7cb70u, 0x3f7e8c9cu, 0xbdd9b933u, 0x80000000u, 0x3d18e1c7u, 0x3eb2bd8eu, 0x3f6fb40au)]
    [InlineData(0x3eda656bu, 0xbf6bbf2fu, 0x3d7b2570u, 0x43b24574u, 0x43936d43u, 0xbf684a5bu, 0xbed731bbu, 0x80000000u, 0xbccfa447u, 0x3d60234du, 0x3f7f88b8u)]
    [InlineData(0x3f54cd21u, 0xbf21f05fu, 0x3ec63ef3u, 0x43a9d4aau, 0x43a15d5bu, 0xbf1b075cu, 0xbf4bb87au, 0x80000000u, 0xbe8d9cf5u, 0x3e57880fu, 0x3f700a2bu)]
    [InlineData(0xbf534352u, 0x00000000u, 0xbf46a5f8u, 0x422cf306u, 0x43340000u, 0xb3bbbd2eu, 0x3f800000u, 0x80000000u, 0xbf2f5d98u, 0xb3809af6u, 0x3f3a8073u)]
    [InlineData(0xbf680347u, 0xbf4ad938u, 0xbf1978a3u, 0x41d3c7f8u, 0x435d29c6u, 0xbf28802du, 0x3f40b9ffu, 0x80000000u, 0xbeabd2d6u, 0xbe9639a9u, 0x3f652860u)]
    [InlineData(0xbeb9a6d7u, 0xbf33cb81u, 0xbee5b728u, 0x41eca5a1u, 0x4372b17au, 0xbf6378e2u, 0x3eeae1d1u, 0x80000000u, 0xbe67e65bu, 0xbee09582u, 0x3f5ea206u)]
    [InlineData(0x00000000u, 0xbf68ac2au, 0x3f11523bu, 0x43a4018fu, 0x43870000u, 0xbf800000u, 0xb24cde2eu, 0x80000000u, 0xb1d90d90u, 0x3f079cf8u, 0x3f592111u)]
    [InlineData(0xbf29efd8u, 0x3ed47b46u, 0xbee507a7u, 0x41edf25au, 0x4313fcbfu, 0x3f07b51eu, 0x3f5911fau, 0x80000000u, 0xbed76241u, 0x3e86a737u, 0x3f5e460fu)]
    [InlineData(0xbf43e89bu, 0x3f74b3e5u, 0x3f414fdfu, 0x43a42b44u, 0x4300ae43u, 0x3f47d815u, 0x3f1ffeaeu, 0x80000000u, 0x3ea7f700u, 0xbed1cc93u, 0x3f59e597u)]
    [InlineData(0xbf3f7015u, 0xbebe86e8u, 0xbed7ce10u, 0x41d635c7u, 0x434e74b6u, 0xbee419bau, 0x3f6530e9u, 0x80000000u, 0xbece812fu, 0xbe4d85a3u, 0x3f648cbeu)]
    public void TheDirectionsAnglesAndBasisAreMathlibsBits(uint fx, uint fy, uint fz, uint pitch, uint yaw,
                                                          uint rx, uint ry, uint rz, uint ux, uint uy, uint uz)
    {
        var forward = new Vector(F(fx), F(fy), F(fz));

        Vector angles = Angles.FromVector(forward);
        var (right, up) = Angles.RightUp(forward);

        Assert.Equal((pitch, yaw, 0u), (B(angles.X), B(angles.Y), B(angles.Z)));
        Assert.Equal((rx, ry, rz), (B(right.X), B(right.Y), B(right.Z)));
        Assert.Equal((ux, uy, uz), (B(up.X), B(up.Y), B(up.Z)));
    }

    private static float F(uint bits) => BitConverter.UInt32BitsToSingle(bits);

    private static uint B(float value) => BitConverter.SingleToUInt32Bits(value);
}
