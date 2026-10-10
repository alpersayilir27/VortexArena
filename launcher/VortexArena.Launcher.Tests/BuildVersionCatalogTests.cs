using System.IO;
using VortexArena.Launcher.Services;
using Xunit;

namespace VortexArena.Launcher.Tests;

/// <summary>
/// Versioned build naming, the unpacked-folder scan, the merge/badge decision and the selection
/// rule — one implementation shared by the admin and server builds.
/// <para>⚠️ A folder counts as installed only with the package's exe in it; a half-unpacked folder
/// would otherwise look startable.</para>
/// </summary>
public class BuildVersionCatalogTests
{
    private static readonly BuildPackage Admin = BuildPackage.Admin;
    private static readonly BuildPackage Server = BuildPackage.Server;

    // ────────────────────────────────────────────────────────────── package names

    [Fact]
    public void PaketAdi_SurumBasinadir()
    {
        Assert.Equal("admin_v57.zip", Admin.FileNameFor(57));
        Assert.Equal("admin_v57", Admin.FolderNameFor(57));
        Assert.Equal("admin_v57.extract", Admin.ExtractFolderNameFor(57));

        Assert.Equal("server_v57.zip", Server.FileNameFor(57));
        Assert.Equal("server_v57", Server.FolderNameFor(57));
        Assert.Equal("server_v57.extract", Server.ExtractFolderNameFor(57));
    }

    [Fact]
    public void PaketExeleri_YayinCiktisiylaAyni()
    {
        Assert.Equal("VortexArena.exe", Admin.ExeName);
        Assert.Equal("VortexArena.Server.App.exe", Server.ExeName);
    }

    [Theory]
    [InlineData("admin_v57.zip", 57)]
    [InlineData("admin_v7.zip", 7)]
    [InlineData("ADMIN_V7.ZIP", 7)]
    public void ZipAdiAyristirilir(string fileName, int expected)
    {
        Assert.Equal(expected, Admin.ParseFileName(fileName));
    }

    [Theory]
    [InlineData("server_v57.zip", 57)]
    [InlineData("SERVER_V7.ZIP", 7)]
    public void SunucuZipAdiAyristirilir(string fileName, int expected)
    {
        Assert.Equal(expected, Server.ParseFileName(fileName));
    }

    [Theory]
    [InlineData("admin_v57.zip.part")]
    [InlineData("admin_v.zip")]
    [InlineData("admin_v12a.zip")]
    [InlineData("game_v12.apk")]
    [InlineData("server_v12.zip")]
    [InlineData("")]
    public void TaninmayanZipAdiNullDoner(string fileName)
    {
        Assert.Null(Admin.ParseFileName(fileName));
    }

    [Theory]
    [InlineData("admin_v57", 57)]
    [InlineData("ADMIN_V57", 57)]
    public void KlasorAdiAyristirilir(string folderName, int expected)
    {
        Assert.Equal(expected, Admin.ParseFolderName(folderName));
    }

    [Theory]
    [InlineData("admin_v57.extract")]
    [InlineData("admin_v57.zip")]
    [InlineData("server_v57")]
    [InlineData("baska")]
    [InlineData("")]
    public void TaninmayanKlasorAdiNullDoner(string folderName)
    {
        Assert.Null(Admin.ParseFolderName(folderName));
    }

    // ──────────────────────────────────────────────────────────────── list parsing

    [Fact]
    public void SurumListesi_DosyaAdiYoksaZipAdiTuretilir()
    {
        const string json = """
        { "versions": [ { "version": 57 } ] }
        """;

        Assert.Equal("admin_v57.zip", HttpVersionSource.ParseList(json, Admin.FileNameFor)[0].File);
        Assert.Equal("server_v57.zip", HttpVersionSource.ParseList(json, Server.FileNameFor)[0].File);
    }

    // ──────────────────────────────────────────────────────────── unpacked folders

    [Fact]
    public void YerelTarama_YalnizExeIcerenKlasorlerSayilir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"va-admin-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "admin_v57"));
            File.WriteAllText(Path.Combine(dir, "admin_v57", "VortexArena.exe"), "x");
            Directory.CreateDirectory(Path.Combine(dir, "admin_v56"));          // no exe
            Directory.CreateDirectory(Path.Combine(dir, "admin_v58.extract"));  // staging
            Directory.CreateDirectory(Path.Combine(dir, "baska"));
            File.WriteAllText(Path.Combine(dir, "admin_v58.zip"), "x");

            var found = BuildVersionCatalog.ReadInstalled(dir, Admin);

            Assert.Equal([57], found.Select(b => b.Version));
            Assert.EndsWith("VortexArena.exe", found[0].Exe);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void YerelTarama_SunucuSurumuServerExesiniArar()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"va-server-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "server_v57"));
            File.WriteAllText(Path.Combine(dir, "server_v57", "VortexArena.Server.App.exe"), "x");
            // Only config\ unpacked so far: the folder must not count as installed.
            Directory.CreateDirectory(Path.Combine(dir, "server_v58", "config"));

            var found = BuildVersionCatalog.ReadInstalled(dir, Server);

            Assert.Equal([57], found.Select(b => b.Version));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ArtiklarTemizlenir_IsleyenSurumeDokunulmaz()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"va-admin-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "admin_v57.zip"), "x");
            File.WriteAllText(Path.Combine(dir, "admin_v58.zip.part"), "x");
            File.WriteAllText(Path.Combine(dir, "notlar.txt"), "x");
            Directory.CreateDirectory(Path.Combine(dir, "admin_v57.extract"));
            Directory.CreateDirectory(Path.Combine(dir, "admin_v58.extract"));

            BuildVersionCatalog.CleanLeftovers(dir, Admin, [58]);

            Assert.False(File.Exists(Path.Combine(dir, "admin_v57.zip")));
            Assert.False(Directory.Exists(Path.Combine(dir, "admin_v57.extract")));
            Assert.True(File.Exists(Path.Combine(dir, "admin_v58.zip.part")));
            Assert.True(Directory.Exists(Path.Combine(dir, "admin_v58.extract")));
            Assert.True(File.Exists(Path.Combine(dir, "notlar.txt")));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ArtiklarTemizlenir_BaskaPaketinArtigiKalir()
    {
        // Each list sweeps only its own folder's naming; a stray name is never deleted.
        var dir = Path.Combine(Path.GetTempPath(), $"va-server-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "server_v57.zip"), "x");
            File.WriteAllText(Path.Combine(dir, "admin_v57.zip"), "x");

            BuildVersionCatalog.CleanLeftovers(dir, Server, []);

            Assert.False(File.Exists(Path.Combine(dir, "server_v57.zip")));
            Assert.True(File.Exists(Path.Combine(dir, "admin_v57.zip")));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    // ───────────────────────────────────────────────────────────── exe path → version

    [Fact]
    public void SurecYolu_SurumKlasorundenOkunur()
    {
        const string versionsDir = @"C:\deploy\server_versions";

        Assert.Equal(57, BuildVersionCatalog.VersionOfExe(
            $@"{versionsDir}\server_v57\VortexArena.Server.App.exe", versionsDir, Server));
    }

    [Theory]
    // pre-version layout, another deployment, and the wrong exe in the right folder
    [InlineData(@"C:\deploy\server\VortexArena.Server.App.exe")]
    [InlineData(@"D:\baska\server_versions\server_v57\VortexArena.Server.App.exe")]
    [InlineData(@"C:\deploy\server_versions\server_v57\baska.exe")]
    [InlineData(null)]
    public void SurecYolu_BizeAitDegilseSifir(string? exePath)
    {
        Assert.Equal(0, BuildVersionCatalog.VersionOfExe(exePath, @"C:\deploy\server_versions", Server));
    }

    // ────────────────────────────────────────────────────────────────────── merge

    private static LocalBuild Local(int version) =>
        new(version,
            $@"C:\deploy\admin_versions\admin_v{version}",
            $@"C:\deploy\admin_versions\admin_v{version}\VortexArena.exe",
            new DateTime(2026, 1, 1));

    private static RemoteVersion Remote(int version) =>
        new(version, Admin.FileNameFor(version), 2048, "2026-01-01 00:00:00");

    [Fact]
    public void Birlesim_UzakVeYerelBirleserekBuyuktenKucuge()
    {
        var rows = BuildVersionCatalog.Merge([Remote(58), Remote(57)], [Local(56)]);

        Assert.Equal([58, 57, 56], rows.Select(r => r.Version));
    }

    [Fact]
    public void Birlesim_UzaktaVarYereldeYokIseIndirilebilir()
    {
        var row = BuildVersionCatalog.Merge([Remote(57)], [])[0];

        Assert.True(row.CanDownload);
        Assert.False(row.Installed);
        Assert.Equal(CloudState.OnServer, row.Cloud);
    }

    [Fact]
    public void Birlesim_AcilmisSurumYenidenIndirilmez()
    {
        var row = BuildVersionCatalog.Merge([Remote(57)], [Local(57)])[0];

        Assert.True(row.Installed);
        Assert.False(row.CanDownload);
        Assert.False(row.CloudWarning);
    }

    [Fact]
    public void BulutCarpi_ListeAlinamadiVeYereldeVar()
    {
        // remote == null = the list could not be fetched; a local build cannot be confirmed.
        var row = BuildVersionCatalog.Merge(null, [Local(57)])[0];

        Assert.Equal(CloudState.ServerUnreachable, row.Cloud);
        Assert.True(row.CloudWarning);
    }

    [Fact]
    public void BulutCarpi_ListeVarAmaSurumListedeYok()
    {
        var row = BuildVersionCatalog.Merge([Remote(58)], [Local(57)])
            .Single(r => r.Version == 57);

        Assert.Equal(CloudState.MissingOnServer, row.Cloud);
        Assert.True(row.CloudWarning);
    }

    // ────────────────────────────────────────────────────────────────── selection

    [Fact]
    public void Secim_OperatorunSectigiSurumAcilmissaOnDedir()
    {
        Assert.Equal(56, BuildVersionCatalog.Select(56, [58, 57, 56]));
    }

    [Fact]
    public void Secim_SecilenSurumYoksaEnYuksekAcilmisSurum()
    {
        Assert.Equal(58, BuildVersionCatalog.Select(55, [58, 57]));
        Assert.Equal(58, BuildVersionCatalog.Select(0, [58, 57]));
    }

    [Fact]
    public void Secim_HicSurumAcilmamissaSifir()
    {
        Assert.Equal(0, BuildVersionCatalog.Select(57, []));
    }

    [Fact]
    public void AcilmisSurumOnbellegi_YenilemeDegisimiBildirir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"va-server-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "server_v57"));
            File.WriteAllText(Path.Combine(dir, "server_v57", "VortexArena.Server.App.exe"), "x");

            var preferred = 0;
            var builds = new InstalledBuilds(dir, Server, () => preferred);

            Assert.Equal([57], builds.Versions);
            Assert.Equal(57, builds.Selected);
            Assert.False(builds.Refresh());

            Directory.CreateDirectory(Path.Combine(dir, "server_v58"));
            File.WriteAllText(Path.Combine(dir, "server_v58", "VortexArena.Server.App.exe"), "x");

            Assert.True(builds.Refresh());
            Assert.Equal(58, builds.Selected);

            preferred = 57;
            Assert.Equal(57, builds.Selected);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
