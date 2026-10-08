using VortexArena.Launcher.Services;
using Xunit;

namespace VortexArena.Launcher.Tests;

/// <summary>
/// adb output parsing. Pure functions on purpose: the states the operator sees
/// (unauthorized/offline) are exactly the ones that cannot be reproduced on demand.
/// </summary>
public class AdbParsingTests
{
    [Fact]
    public void CihazListesi_BannerAtlanirModelOkunur()
    {
        const string output = """
        List of devices attached
        1WMHH8123456           device product:eureka model:Quest_3 device:eureka transport_id:3

        """;

        var devices = AdbService.ParseDevices(output);

        var device = Assert.Single(devices);
        Assert.Equal("1WMHH8123456", device.Serial);
        Assert.Equal("Quest 3", device.Model);
        Assert.True(device.IsReady);
    }

    [Fact]
    public void CihazListesi_YetkisizVeCevrimdisiDurumlariKorunur()
    {
        const string output = """
        List of devices attached
        AAA111                 unauthorized
        BBB222                 offline
        """;

        var devices = AdbService.ParseDevices(output);

        Assert.Equal(2, devices.Count);
        Assert.Equal(AdbDevice.StateUnauthorized, devices[0].State);
        Assert.False(devices[0].IsReady);
        Assert.Equal(AdbDevice.StateOffline, devices[1].State);
    }

    [Fact]
    public void CihazListesi_SunucuMesajlariAtlanir()
    {
        const string output = """
        * daemon not running; starting now at tcp:5037
        * daemon started successfully
        List of devices attached
        """;

        Assert.Empty(AdbService.ParseDevices(output));
    }

    [Fact]
    public void PaketListesi_YalnizKendiPaketlerimizBuyuktenKucuge()
    {
        const string output = """
        package:com.vortex.arenav130
        package:com.vortex.arenav132
        package:com.vortex.arena
        package:com.baska.oyun
        package:com.vortex.arenavX
        """;

        Assert.Equal([132, 130], AdbService.ParseInstalledVersions(output));
    }

    [Fact]
    public void PaketListesi_BosCiktiBosListe()
    {
        Assert.Empty(AdbService.ParseInstalledVersions(""));
    }
}
