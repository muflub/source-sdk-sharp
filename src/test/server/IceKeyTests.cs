using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="IceKey"/>, the port of <c>src/mathlib/IceKey.cpp</c>, against
/// vectors it did not produce: Kwan's published ones
/// (https://darkside.com.au/ice/overview.html, plaintext
/// <c>fedcba9876543210</c>) at all three levels, and the first block of the
/// shipped <c>scripts/playerclasses/scout.ctx</c> (tf2_misc_dir.vpk), whose
/// plaintext is the file's opening <c>//\r\n// T</c>.
/// </summary>
public class IceKeyTests
{
    private const string Plain = "fedcba9876543210";

    [Theory]
    [InlineData(0, "deadbeef01234567", "de240d83a00a9cc0")]
    [InlineData(1, "deadbeef01234567", "7d6ef1ef30d47a96")]
    [InlineData(2, "00112233445566778899aabbccddeeff", "f94840d86972f21c")]
    public void Encrypt_gives_the_published_ciphertext(int level, string key, string cipher)
        => Assert.Equal(cipher, Convert.ToHexStringLower(Encrypt(level, key, Plain)));

    [Theory]
    [InlineData(0, "deadbeef01234567", "de240d83a00a9cc0")]
    [InlineData(1, "deadbeef01234567", "7d6ef1ef30d47a96")]
    [InlineData(2, "00112233445566778899aabbccddeeff", "f94840d86972f21c")]
    public void Decrypt_gives_the_published_plaintext_back(int level, string key, string cipher)
    {
        var ice = new IceKey(level);
        ice.Set(Convert.FromHexString(key));
        var plain = new byte[8];
        ice.Decrypt(Convert.FromHexString(cipher), plain);
        Assert.Equal(Plain, Convert.ToHexStringLower(plain));
    }

    [Fact]
    public void The_first_block_of_the_shipped_scout_ctx_decrypts_to_its_text_under_the_TF_key()
    {
        // GetTFEncryptionKey() (tf_shareddefs.cpp) is "E2NcUkG2".
        var ice = new IceKey(0);
        ice.Set("E2NcUkG2"u8);
        var plain = new byte[8];
        ice.Decrypt(Convert.FromHexString("684f06a21d859d66"), plain);
        Assert.Equal("//\r\n// T"u8.ToArray(), plain);
    }

    [Fact]
    public void Level_zero_is_an_eight_byte_key_and_level_two_a_sixteen_byte_one()
    {
        Assert.Equal(8, new IceKey(0).KeySize);
        Assert.Equal(16, new IceKey(2).KeySize);
        Assert.Equal(8, new IceKey(2).BlockSize);
    }

    [Fact]
    public void A_key_of_the_wrong_length_is_refused()
        => Assert.Throws<ArgumentException>(() => new IceKey(0).Set("short"u8));

    private static byte[] Encrypt(int level, string key, string plainHex)
    {
        var ice = new IceKey(level);
        ice.Set(Convert.FromHexString(key));
        var cipher = new byte[8];
        ice.Encrypt(Convert.FromHexString(plainHex), cipher);
        return cipher;
    }
}
