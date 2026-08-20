using System.IO;
using VortexArena.Launcher;
using Xunit;

namespace VortexArena.Launcher.Tests;

/// <summary>
/// Argument contract + validation tests.
/// <para>
/// ⚠️ These argument names must match <b>two separate code bases</b> exactly:
/// <c>--server-ip</c>/<c>--server-port</c> → Unity <c>AppBoot.ArgServerIp</c>/<c>ArgServerPort</c>,
/// <c>--venue</c> → the server's <c>Program.SelectVenue</c>. A failure here means the launcher or
/// the other side was changed alone.
/// </para>
/// </summary>
public class LauncherConfigTests
{
    /// <summary>Used wherever a test needs an "existing file".</summary>
    private static string ExistingFile => Environment.ProcessPath!;

    [Fact]
    public void ArgumanAdlari_UnityVeSunucuSozlesmesiyleAyni()
    {
        Assert.Equal("--server-ip", LauncherConfig.ArgServerIp);
        Assert.Equal("--server-port", LauncherConfig.ArgServerPort);
        Assert.Equal("--venue", LauncherConfig.ArgVenue);
    }

    [Fact]
    public void VarsayilanPort_ProtokolunWsKontrolPortudur()
    {
        Assert.Equal(47821, LauncherConfig.DefaultPort);
    }

    [Fact]
    public void GameArguments_AppBootSozlesmesineUyar()
    {
        var config = new LauncherConfig
        {
            AdminExePath = @"C:\deploy\admin\VortexArena.exe",
            ServerIp = "192.168.1.10",
            ServerPort = 47821,
        };

        Assert.Equal(
            ["--server-ip", "192.168.1.10", "--server-port", "47821"],
            config.GameArguments);
    }

    [Fact]
    public void GameArguments_IpBosluklariniKirpar()
    {
        var config = new LauncherConfig { ServerIp = "  10.0.0.5  " };
        Assert.Equal("10.0.0.5", config.GameArguments[1]);
    }

    [Fact]
    public void ServerArguments_MekaniVenueOlarakGecer()
    {
        var config = new LauncherConfig { Venue = "VortexAntep" };
        Assert.Equal(["--venue", "VortexAntep"], config.ServerArguments);
    }

    [Fact]
    public void ServerArguments_MekanBosaBosDoner()
    {
        // Starting with an empty venue is already blocked in ValidateServer; this asserts no
        // silently wrong argument is produced.
        Assert.Empty(new LauncherConfig { Venue = "   " }.ServerArguments);
    }

    [Theory]
    [InlineData("192.168.1.10", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("  10.0.0.5  ", true)]
    [InlineData("192.168.1", false)]
    [InlineData("arena-pc", false)]
    [InlineData("", false)]
    public void IpDogrulamasi(string value, bool expected)
    {
        Assert.Equal(expected, LauncherConfig.IsValidIp(value));
    }

    [Theory]
    [InlineData(47821, true)]
    [InlineData(0, false)]
    [InlineData(70000, false)]
    public void PortDogrulamasi(int value, bool expected)
    {
        Assert.Equal(expected, LauncherConfig.IsValidPort(value));
    }

    [Fact]
    public void Validate_ExeSecilmediyseSebepDoner()
    {
        var problem = new LauncherConfig { ServerIp = "127.0.0.1" }.Validate();
        Assert.Contains("Admin exe", problem);
    }

    [Fact]
    public void Validate_ExeYoluVarAmaDosyaYoksaYakalanir()
    {
        var problem = new LauncherConfig
        {
            AdminExePath = @"C:\olmayan\klasor\VortexArena.exe",
            ServerIp = "127.0.0.1",
        }.Validate();

        Assert.Contains("bulunamadı", problem);
    }

    [Fact]
    public void Validate_GecersizIpYakalanir()
    {
        var problem = new LauncherConfig
        {
            AdminExePath = ExistingFile,
            ServerIp = "bozuk",
        }.Validate();

        Assert.Contains("Geçersiz IP", problem);
    }

    [Fact]
    public void Validate_HerSeyYerindeyseNull()
    {
        var problem = new LauncherConfig
        {
            AdminExePath = ExistingFile,
            ServerIp = "192.168.1.10",
        }.Validate();

        Assert.Null(problem);
    }

    // ────────────────────────────────────────────────────── server validation

    [Fact]
    public void ValidateServer_ExeSecilmediyseSebepDoner()
    {
        var problem = new LauncherConfig().ValidateServer([]);
        Assert.Contains("Sunucu exe", problem);
    }

    [Fact]
    public void ValidateServer_MekanSecilmedenBaslatmaYok()
    {
        // The rule being guarded: started without a venue, the server silently opens the
        // alphabetically first one.
        var problem = new LauncherConfig { ServerExePath = ExistingFile }
            .ValidateServer(["Outdoor12x12", "VortexAntep"]);

        Assert.Contains("Mekan seçilmedi", problem);
    }

    [Fact]
    public void ValidateServer_MapsJsonOkunamadiysaElleYazmayiIster()
    {
        var problem = new LauncherConfig { ServerExePath = ExistingFile }.ValidateServer([]);
        Assert.Contains("elle yazın", problem);
    }

    [Fact]
    public void ValidateServer_TaninmayanMekanYakalanir()
    {
        var problem = new LauncherConfig { ServerExePath = ExistingFile, Venue = "Yok" }
            .ValidateServer(["Outdoor12x12"]);

        Assert.Contains("maps.json", problem);
    }

    [Fact]
    public void ValidateServer_BuyukKucukHarfDuyarsizEslesir()
    {
        var problem = new LauncherConfig { ServerExePath = ExistingFile, Venue = "vortexantep" }
            .ValidateServer(["Outdoor12x12", "VortexAntep"]);

        Assert.Null(problem);
    }

    // ─────────────────────────────────────────────────────────────── persistence

    [Fact]
    public void KaydetVeYukle_TumAlanlariKorur()
    {
        var path = Path.Combine(Path.GetTempPath(), $"va-launcher-{Guid.NewGuid():N}", "settings.json");
        try
        {
            new LauncherConfig
            {
                AdminExePath = @"C:\deploy\admin\VortexArena.exe",
                ServerExePath = @"C:\deploy\server\VortexArena.Server.App.exe",
                ServerIp = "192.168.1.50",
                ServerPort = 47821,
                Venue = "VortexAntep",
            }.Save(path);

            var loaded = LauncherConfig.Load(path);

            Assert.Equal(@"C:\deploy\admin\VortexArena.exe", loaded.AdminExePath);
            Assert.Equal(@"C:\deploy\server\VortexArena.Server.App.exe", loaded.ServerExePath);
            Assert.Equal("192.168.1.50", loaded.ServerIp);
            Assert.Equal(47821, loaded.ServerPort);
            Assert.Equal("VortexAntep", loaded.Venue);
        }
        finally
        {
            var dir = Path.GetDirectoryName(path);
            if (dir != null && Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Yukle_DosyaYoksaVarsayilanlarlaDoner()
    {
        var loaded = LauncherConfig.Load(Path.Combine(Path.GetTempPath(), $"yok-{Guid.NewGuid():N}.json"));

        Assert.Equal("127.0.0.1", loaded.ServerIp);
        Assert.Equal(LauncherConfig.DefaultPort, loaded.ServerPort);
        Assert.Equal("", loaded.Venue);
    }
}
