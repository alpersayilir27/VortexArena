using System.IO;
using VortexArena.Launcher.Services;
using Xunit;

namespace VortexArena.Launcher.Tests;

/// <summary>
/// Argument contract + validation tests.
/// <para>
/// ⚠️ These argument names must match <b>two separate code bases</b> exactly:
/// <c>--server-ip</c>/<c>--server-port</c>/<c>--replay</c> → Unity <c>AppBoot</c>,
/// <c>--venue</c>/<c>--replay-dir</c> → the server. A failure here means one side was changed alone.
/// </para>
/// </summary>
public class LauncherSettingsTests
{
    [Fact]
    public void ArgumanAdlari_UnityVeSunucuSozlesmesiyleAyni()
    {
        Assert.Equal("--server-ip", LauncherSettings.ArgServerIp);
        Assert.Equal("--server-port", LauncherSettings.ArgServerPort);
        Assert.Equal("--replay", LauncherSettings.ArgReplay);
        Assert.Equal("--venue", LauncherSettings.ArgVenue);
        Assert.Equal("--replay-dir", LauncherSettings.ArgReplayDir);
    }

    [Fact]
    public void VarsayilanPort_ProtokolunKontrolPortudur()
    {
        Assert.Equal(47821, LauncherSettings.DefaultPort);
    }

    [Fact]
    public void AdminArgumanlari_AppBootSozlesmesineUyar()
    {
        var settings = new LauncherSettings { ServerIp = "192.168.1.10" };

        Assert.Equal(
            ["--server-ip", "192.168.1.10", "--server-port", "47821"],
            settings.AdminArguments(47821));
    }

    [Fact]
    public void AdminArgumanlari_IpBosluklariniKirpar()
    {
        Assert.Equal("10.0.0.5", new LauncherSettings { ServerIp = "  10.0.0.5  " }.AdminArguments(1)[1]);
    }

    [Fact]
    public void SunucuArgumanlari_MekanVeKayitKlasoruGecer()
    {
        Assert.Equal(
            ["--venue", "VortexAntep", "--replay-dir", @"C:\deploy\replays"],
            LauncherSettings.ServerArguments("VortexAntep", @"C:\deploy\replays"));
    }

    [Fact]
    public void SunucuArgumanlari_MekanBosaBosDoner()
    {
        // An empty venue is already blocked by ValidateVenue; this asserts no silently wrong
        // argument list is produced.
        Assert.Empty(LauncherSettings.ServerArguments("   ", @"C:\x"));
    }

    [Fact]
    public void KayitArgumanlari_AppBootReplayArgumaniniKullanir()
    {
        Assert.Equal(["--replay", @"C:\r\a.vxr"], LauncherSettings.ReplayArguments(@"C:\r\a.vxr"));
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
        Assert.Equal(expected, LauncherSettings.IsValidIp(value));
    }

    [Theory]
    [InlineData(47821, true)]
    [InlineData(0, false)]
    [InlineData(70000, false)]
    public void PortDogrulamasi(int value, bool expected)
    {
        Assert.Equal(expected, LauncherSettings.IsValidPort(value));
    }

    // ───────────────────────────────────────────────────────── venue validation

    [Fact]
    public void MekanSecilmedenBaslatmaYok()
    {
        // The rule being guarded: started without a venue, the server silently opens the
        // alphabetically first one.
        var problem = LauncherSettings.ValidateVenue("", ["Outdoor12x12", "VortexAntep"]);
        Assert.Contains("Mekan seçilmedi", problem);
    }

    [Fact]
    public void TaninmayanMekanYakalanir()
    {
        Assert.Contains("maps.json", LauncherSettings.ValidateVenue("Yok", ["Outdoor12x12"]));
    }

    [Fact]
    public void MekanBuyukKucukHarfDuyarsizEslesir()
    {
        Assert.Null(LauncherSettings.ValidateVenue("vortexantep", ["Outdoor12x12", "VortexAntep"]));
    }

    // ──────────────────────────────────────────────────────────── control port

    [Fact]
    public void ControlPort_ServerJsonundanOkunur()
    {
        Assert.Equal(12345, LauncherSettings.ParseControlPort("""{ "controlPort": 12345 }"""));
    }

    [Theory]
    [InlineData("""{ "beaconPort": 1 }""")]
    [InlineData("""{ "controlPort": "47821" }""")]
    [InlineData("""{ "controlPort": 0 }""")]
    [InlineData("bu json degil")]
    public void ControlPort_OkunamazsaNull(string json)
    {
        Assert.Null(LauncherSettings.ParseControlPort(json));
    }

    [Fact]
    public void ControlPort_OverrideServerJsonunUstundedir()
    {
        var settings = new LauncherSettings { ControlPortOverride = 5000 };
        Assert.Equal(5000, settings.ResolveControlPort(@"C:\olmayan\server.json"));
    }

    [Fact]
    public void ControlPort_HicbirKaynakYoksaVarsayilan()
    {
        Assert.Equal(
            LauncherSettings.DefaultPort,
            new LauncherSettings().ResolveControlPort(@"C:\olmayan\server.json"));
    }

    // ─────────────────────────────────────────────────────────────── persistence

    [Fact]
    public void KaydetVeYukle_TumAlanlariKorur()
    {
        var path = Path.Combine(Path.GetTempPath(), $"va-launcher-{Guid.NewGuid():N}", "settings.json");
        try
        {
            new LauncherSettings
            {
                Venue = "VortexAntep",
                ControlPortOverride = 47999,
                ServerIp = "192.168.1.50",
                VersionsUrl = "http://ornek/versions",
                DownloadBaseUrl = "http://ornek/files/",
                PreferredDeviceSerial = "1WMHH000",
            }.Save(path);

            var loaded = LauncherSettings.Load(path);

            Assert.Equal("VortexAntep", loaded.Venue);
            Assert.Equal(47999, loaded.ControlPortOverride);
            Assert.Equal(LauncherSettings.DefaultServerIp, loaded.ServerIp);
            Assert.DoesNotContain("serverIp", File.ReadAllText(path));
            Assert.Equal("http://ornek/versions", loaded.VersionsUrl);
            Assert.Equal("http://ornek/files/", loaded.DownloadBaseUrl);
            Assert.Equal("1WMHH000", loaded.PreferredDeviceSerial);
        }
        finally
        {
            var dir = Path.GetDirectoryName(path);
            if (dir != null && Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Yukle_EskiDosyadakiBilinmeyenAlanlarSorunCikarmaz()
    {
        // An older settings.json carried serverExePath/adminExePath/serverIp; those keys are gone
        // and must not break loading. A stored serverIp is ignored: every launch starts on loopback.
        var path = Path.Combine(Path.GetTempPath(), $"va-launcher-{Guid.NewGuid():N}", "settings.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """
            {
              "adminExePath": "C:\\eski\\VortexArena.exe",
              "serverExePath": "C:\\eski\\VortexArena.Server.App.exe",
              "serverIp": "192.168.1.7",
              "serverPort": 47821,
              "venue": "VortexAntep"
            }
            """);

            var loaded = LauncherSettings.Load(path);

            Assert.Equal(LauncherSettings.DefaultServerIp, loaded.ServerIp);
            Assert.Equal("VortexAntep", loaded.Venue);
            Assert.Equal(LauncherSettings.DefaultVersionsUrl, loaded.VersionsUrl);
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
        var loaded = LauncherSettings.Load(Path.Combine(Path.GetTempPath(), $"yok-{Guid.NewGuid():N}.json"));

        Assert.Equal("127.0.0.1", loaded.ServerIp);
        Assert.Equal(0, loaded.ControlPortOverride);
        Assert.Equal("", loaded.Venue);
        Assert.Equal(LauncherSettings.DefaultDownloadBaseUrl, loaded.DownloadBaseUrl);
    }
}
