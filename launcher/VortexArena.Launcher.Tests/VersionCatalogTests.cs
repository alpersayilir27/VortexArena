using VortexArena.Launcher.Services;
using Xunit;

namespace VortexArena.Launcher.Tests;

/// <summary>
/// Version listing, APK naming and the merge/badge decision.
/// <para>⚠️ The crossed-out-cloud rule has two different causes that must stay distinguishable:
/// the server does not list the version, or the list could not be fetched at all.</para>
/// </summary>
public class VersionCatalogTests
{
    private const string SampleJson = """
    {
      "count": 2,
      "versions": [
        { "version": 132, "file": "game_v132.apk", "size": 1048576, "modified": "2026-01-02 03:04:05" },
        { "version": 130, "file": "game_v130.apk", "size": 2097152, "modified": "2026-01-01 03:04:05" }
      ]
    }
    """;

    // ────────────────────────────────────────────────────────────── package names

    [Fact]
    public void PaketAdi_SurumBasinadir()
    {
        Assert.Equal("com.vortex.arenav132", GamePackage.NameFor(132));
        Assert.Equal("game_v132.apk", GamePackage.FileNameFor(132));
    }

    [Theory]
    [InlineData("game_v132.apk", 132)]
    [InlineData("game_v7.apk", 7)]
    [InlineData("GAME_V7.APK", 7)]
    public void ApkAdiAyristirilir(string fileName, int expected)
    {
        Assert.Equal(expected, GamePackage.ParseFileName(fileName));
    }

    [Theory]
    [InlineData("game_v132.apk.part")]
    [InlineData("game_v.apk")]
    [InlineData("game_v12a.apk")]
    [InlineData("baska.apk")]
    [InlineData("")]
    public void TaninmayanApkAdiNullDoner(string fileName)
    {
        Assert.Null(GamePackage.ParseFileName(fileName));
    }

    // ──────────────────────────────────────────────────────────────── list parsing

    [Fact]
    public void SurumListesi_YenidenEskiyeSiralanir()
    {
        var parsed = HttpVersionSource.ParseList(SampleJson);

        Assert.Equal([132, 130], parsed.Select(v => v.Version));
        Assert.Equal(1048576, parsed[0].Size);
        Assert.Equal("2026-01-02 03:04:05", parsed[0].Modified);
    }

    [Fact]
    public void SurumListesi_BozukGirdiAtlanirListeKalir()
    {
        const string json = """
        { "versions": [ { "file": "x.apk" }, { "version": 9 } ] }
        """;

        var parsed = HttpVersionSource.ParseList(json);

        Assert.Single(parsed);
        Assert.Equal("game_v9.apk", parsed[0].File);
    }

    [Fact]
    public void SurumListesi_VersionsDizisiYoksaHata()
    {
        Assert.ThrowsAny<Exception>(() => { HttpVersionSource.ParseList("""{ "count": 0 }"""); });
    }

    // ────────────────────────────────────────────────────────────────────── merge

    private static LocalApk Local(int version) =>
        new(version, $@"C:\deploy\game_versions\game_v{version}.apk", 1024, new DateTime(2026, 1, 1));

    private static RemoteVersion Remote(int version) =>
        new(version, GamePackage.FileNameFor(version), 2048, "2026-01-01 00:00:00");

    [Fact]
    public void Birlesim_UzakYerelVeGozlukBirleserekBuyuktenKucuge()
    {
        var rows = VersionCatalog.Merge([Remote(130)], [Local(120)], [140]);

        Assert.Equal([140, 130, 120], rows.Select(r => r.Version));
    }

    [Fact]
    public void Birlesim_YalnizGozluktekiSurumdeYukluRozetiVarIndirmePasif()
    {
        var row = VersionCatalog.Merge([], [], [140])[0];

        Assert.True(row.Installed);
        Assert.False(row.CanDownload);
        Assert.False(row.CanInstall);
    }

    [Fact]
    public void BulutCarpi_ListeAlinamadiVeYereldeVar()
    {
        // remote == null = the list could not be fetched; a local copy cannot be confirmed.
        var row = VersionCatalog.Merge(null, [Local(120)], [])[0];

        Assert.Equal(CloudState.ServerUnreachable, row.Cloud);
        Assert.True(row.CloudWarning);
    }

    [Fact]
    public void BulutCarpi_ListeVarAmaSurumListedeYok()
    {
        var row = VersionCatalog.Merge([Remote(130)], [Local(120)], [])
            .Single(r => r.Version == 120);

        Assert.Equal(CloudState.MissingOnServer, row.Cloud);
        Assert.True(row.CloudWarning);
    }

    [Fact]
    public void BulutCarpi_ListeVarVeSurumHemUzaktaHemYerelde()
    {
        var row = VersionCatalog.Merge([Remote(130)], [Local(130)], [])[0];

        Assert.Equal(CloudState.OnServer, row.Cloud);
        Assert.False(row.CloudWarning);
        Assert.False(row.CanDownload);
        Assert.True(row.CanInstall);
    }

    [Fact]
    public void Birlesim_UzaktaVarYereldeYokIseIndirilebilir()
    {
        var row = VersionCatalog.Merge([Remote(130)], [], [])[0];

        Assert.True(row.CanDownload);
        Assert.False(row.Local);
    }

    // ─────────────────────────────────────────────────────────── install errors

    [Fact]
    public void ImzaCakismasiTaninir()
    {
        Assert.True(AdbInstallError.IsSignatureConflict(
            "Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE: Package ...]"));
        Assert.False(AdbInstallError.IsSignatureConflict("Success"));
    }

    [Fact]
    public void YerYokHatasiTurkceyeCevrilir()
    {
        Assert.Equal("Gözlükte yer yok.",
            AdbInstallError.Translate("Failure [INSTALL_FAILED_INSUFFICIENT_STORAGE]"));
        Assert.Null(AdbInstallError.Translate("Failure [INSTALL_FAILED_BILINMEYEN]"));
    }
}
