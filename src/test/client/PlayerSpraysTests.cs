using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.TempEnts;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="PlayerSprays"/>, from stock c_te_playerdecal.cpp:
/// <c>C_TEPlayerDecal::PostDataUpdate</c> (<c>r_decals</c>),
/// <c>TE_PlayerDecal</c> (<c>cl_spraydisable</c>, the target, the player) and
/// <c>CreateTempMaterialForPlayerLogo</c> (the logo, its names and the download
/// copy).
/// </summary>
public class PlayerSpraysTests
{
    private static readonly Vector Where = new(-16f, 512f, 72f);

    // 0x11223345 in memory order is 45 33 22 11: the first two digits differ, so a swapped folder name is seen.
    private const uint Logo = 0x11223345;
    private const string Hex = "45332211";

    private sealed class Engine : IFwdEngineClient
    {
        public uint LogoFile = Logo;
        public bool PlayerKnown = true;
        public bool CopyAnswer = true;
        public readonly List<(string From, string To)> Copies = [];

        public bool TryGetPlayerInfo(int entIndex, out ClientPlayerInfo info)
        {
            info = new ClientPlayerInfo("player", 2, false, "", LogoFile);
            return PlayerKnown;
        }

        public bool CopyLocalFile(string source, string destination)
        {
            Copies.Add((source, destination));
            return CopyAnswer;
        }

        public void SubmitConsoleLine(string line) { }
        public void SpectatorCommand(SpectatorKeyCommand command) { }
        public void SpectatorServerCommand(string line) { }
        public bool IsInGame() => true;
        public void VoiceServerCommand(string line, bool reliable) { }
        public int SentenceIndexFromName(string name) => -1;
        public int LocalPlayerSlot() => 0;
        public string? LevelName() => null;
        public string? GameDirectory() => null;
        public uint ProtocolVersion() => 0;
        public void JoinTeam(int team) { }
        public void ConnectFromSteamLaunch(string connectLine) { }
        public void Reconnect() { }
        public void RequestManagedReload() { }
        public void ConsolePrintAt(int position, string text) { }
        public void ConsolePrintAt(int position, float timeToLive, float red, float green, float blue, bool fixedWidthFont, string text) { }
        public bool TryLightForPoint(in Vector position, bool clamp, out Vector light) { light = default; return false; }
        public bool TryLightForPointFast(in Vector position, bool clamp, out Vector light) { light = default; return false; }
    }

    private sealed class Files : IFwdBaseFileSystem
    {
        public readonly HashSet<string> Present = [];
        public readonly List<(string Name, string? PathId)> Asked = [];

        public bool FileExists(string fileName, string? pathId)
        {
            Asked.Add((fileName, pathId));
            return Present.Contains(fileName);
        }

        public byte[]? ReadAll(string fileName, string pathId) => null;
        public uint FileSize(string fileName, string pathId) => 0;
        public nint Open(string fileName, string options, string pathId) => 0;
        public int Write(ReadOnlySpan<byte> bytes, nint handle) => 0;
        public void Close(nint handle) { }
    }

    private sealed class Spray : IClientSprayEngine
    {
        public readonly List<(Vector Position, int Player, int Entity, string Material, string Texture)> Calls = [];

        public void PlayerLogoDecal(Vector position, int player, int entity, string material, string texture)
            => Calls.Add((position, player, entity, material, texture));
    }

    private sealed class Harness
    {
        public readonly Engine Engine = new();
        public readonly Files Files = new();
        public readonly Spray Spray = new();
        public readonly RecordingEngine Fake = TestServices.Fake();
        public bool Decals = true, Disabled;
        public readonly PlayerSprays Sprays;

        public Harness()
        {
            Fake.Returns("Effects.ClientEntityOrigin", true).Returns("Effects.ClientEntityOrigin.Origin", Where);
            Sprays = new PlayerSprays(new DecalSettings(() => Decals, () => true), () => Disabled, Engine, Files,
                                      Fake, Spray);
        }

        public SprayOutcome Shoot(int player = 5, int entity = 21) => Sprays.Spray(Where, player, entity);
    }

    [Fact]
    public void ADownloadedLogoIsSprayedWithThePlayersMaterialAndItsTexture()
    {
        var h = new Harness();
        h.Files.Present.Add($"materials/temp/{Hex}.vtf");

        Assert.Equal(SprayOutcome.Sprayed, h.Shoot(player: 5, entity: 21));

        var call = h.Spray.Calls.Single();
        Assert.Equal((Where, 5, 21), (call.Position, call.Player, call.Entity));
        Assert.Equal("decals/playerlogo05", call.Material);
        Assert.Equal($"temp/{Hex}", call.Texture);
        Assert.Empty(h.Engine.Copies);
    }

    /// <summary>
    /// player_info_t's customFiles is at 112 (shell_te_backends_client.cpp's
    /// COMPILE_TIME_ASSERT says the same of the C++), and the record is 132 bytes.
    /// </summary>
    [Fact]
    public void TheLogoCrcIsReadAtPlayerInfosCustomFiles()
    {
        Assert.Equal(112, (int)System.Runtime.InteropServices.Marshal.OffsetOf<PlayerInfoT>(nameof(PlayerInfoT.CustomFile0)));
        Assert.Equal(132, System.Runtime.InteropServices.Marshal.SizeOf<PlayerInfoT>());
    }

    [Fact]
    public void TheLogoMaterialIsTheSlotInTwoDigits()
    {
        Assert.Equal("decals/playerlogo01", PlayerSprays.LogoMaterial(1));
        Assert.Equal("decals/playerlogo64", PlayerSprays.LogoMaterial(64));
    }

    [Fact]
    public void TheFilesAreAskedOfEverySearchPath()
    {
        var h = new Harness();
        h.Shoot();

        Assert.All(h.Files.Asked, a => Assert.Null(a.PathId));
    }

    [Fact]
    public void ALogoInTheDownloadFolderIsCopiedUnderMaterialsFirst()
    {
        var h = new Harness();
        h.Files.Present.Add($"download/user_custom/45/{Hex}.dat");

        Assert.Equal(SprayOutcome.Sprayed, h.Shoot());

        Assert.Equal(($"download/user_custom/45/{Hex}.dat", $"materials/temp/{Hex}.vtf"), h.Engine.Copies.Single());
        Assert.Single(h.Spray.Calls);
    }

    [Fact]
    public void ALogoNotYetDownloadedIsNotSprayed()
    {
        var h = new Harness();

        Assert.Equal(SprayOutcome.NotDownloaded, h.Shoot());
        Assert.Empty(h.Spray.Calls);
        Assert.Empty(h.Engine.Copies);
    }

    [Fact]
    public void ACopyThatFailsIsNotSprayed()
    {
        var h = new Harness();
        h.Files.Present.Add($"download/user_custom/45/{Hex}.dat");
        h.Engine.CopyAnswer = false;

        Assert.Equal(SprayOutcome.CopyFailed, h.Shoot());
        Assert.Empty(h.Spray.Calls);
    }

    [Fact]
    public void APlayerWithNoLogoIsNotSprayed()
    {
        var h = new Harness();
        h.Engine.LogoFile = 0;

        Assert.Equal(SprayOutcome.NoLogo, h.Shoot());
        Assert.Empty(h.Files.Asked);
    }

    [Fact]
    public void RDecalsOffSpraysNothingAndAsksNothing()
    {
        var h = new Harness { Decals = false };

        Assert.Equal(SprayOutcome.DecalsOff, h.Shoot());
        Assert.Empty(h.Fake.Calls);
        Assert.Empty(h.Files.Asked);
    }

    [Fact]
    public void ClSprayDisableSpraysNothing()
    {
        var h = new Harness { Disabled = true };

        Assert.Equal(SprayOutcome.SpraysOff, h.Shoot());
        Assert.Empty(h.Spray.Calls);
    }

    [Fact]
    public void ATargetThisClientDoesNotHaveIsNotSprayedOrDownloaded()
    {
        var h = new Harness();
        h.Fake.Returns("Effects.ClientEntityOrigin", false);
        h.Files.Present.Add($"download/user_custom/45/{Hex}.dat");

        Assert.Equal(SprayOutcome.NoEntity, h.Shoot());
        Assert.Empty(h.Engine.Copies);
    }

    [Fact]
    public void PlayerZeroAndANegativeEntityAreRefused()
    {
        var h = new Harness();

        Assert.Equal(SprayOutcome.Refused, h.Shoot(player: 0));
        Assert.Equal(SprayOutcome.Refused, h.Shoot(entity: -1));
        Assert.Empty(h.Fake.Calls);
    }

    [Fact]
    public void APlayerTheEngineDoesNotKnowIsNotSprayed()
    {
        var h = new Harness();
        h.Engine.PlayerKnown = false;

        Assert.Equal(SprayOutcome.NoPlayer, h.Shoot());
        Assert.Empty(h.Spray.Calls);
    }
}
