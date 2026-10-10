using System.IO;
using VortexArena.Launcher.Services;
using Xunit;

namespace VortexArena.Launcher.Tests;

/// <summary>
/// Root resolution. ⚠️ The exe folder comes from <c>AppContext.BaseDirectory</c>, never
/// <c>Assembly.Location</c> — the latter is empty in a single-file build.
/// </summary>
public class LauncherPathsTests
{
    [Fact]
    public void KokArgumani_HerSeyinUstundedir()
    {
        var root = Path.Combine(Path.GetTempPath(), $"va-root-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "server"));

            var paths = LauncherPaths.Resolve(["--root", root]);

            Assert.Equal(root, paths.Root);
            Assert.Equal(Path.Combine(root, "server_versions"), paths.ServerVersionsDir);
            Assert.Equal(Path.Combine(root, "server_versions", "server_v57"), paths.ServerVersionDir(57));
            Assert.Equal(
                Path.Combine(root, "server_versions", "server_v57", "VortexArena.Server.App.exe"),
                paths.ServerExeFor(57));
            Assert.Equal(
                Path.Combine(root, "server_versions", "server_v57", "config", "maps.json"),
                paths.ServerMapsJsonFor(57));
            Assert.Equal(
                Path.Combine(root, "server_versions", "server_v57", "config", "server.json"),
                paths.ServerVersionConfigJson(57));
            Assert.Equal(
                Path.Combine(root, "server_versions", "server_v57", "firewall-kur.cmd"),
                paths.ServerFirewallScriptFor(57));
            Assert.Equal(Path.Combine(root, "server", "VortexArena.Server.App.exe"), paths.LegacyServerExe);
            Assert.Equal(Path.Combine(root, "admin_versions"), paths.AdminVersionsDir);
            Assert.Equal(Path.Combine(root, "admin_versions", "admin_v57"), paths.AdminVersionDir(57));
            Assert.Equal(
                Path.Combine(root, "admin_versions", "admin_v57", "VortexArena.exe"),
                paths.AdminExeFor(57));
            Assert.Equal(Path.Combine(root, "game_versions"), paths.GameVersionsDir);
            Assert.Equal(Path.Combine(root, "replays"), paths.ReplaysDir);

            // Venue DATA stays outside the version folders (--data-dir).
            Assert.Equal(Path.Combine(root, "server"), paths.ServerDir);
            Assert.Equal(Path.Combine(root, "server", "config", "server.json"), paths.ServerConfigJson);
            Assert.Equal(Path.Combine(root, "server", "logs"), paths.ServerLogsDir);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void KokExeKlasorudur_UstKlasordekiYerlesimeBakilmaz()
    {
        // Shipped layout: deploy\launcher\ holds the exe AND server\, admin_versions\,
        // game_versions\, replays\.
        var root = Path.Combine(Path.GetTempPath(), $"va-root-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "server"));
            var exeDir = Directory.CreateDirectory(Path.Combine(root, "launcher")).FullName;

            var paths = LauncherPaths.Resolve([], exeDir + Path.DirectorySeparatorChar);

            Assert.Equal(exeDir, paths.Root);
            Assert.Equal(Path.Combine(exeDir, "replays"), paths.ReplaysDir);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void YeniKurulumda_KlasorlerExeninYanindaAcilir()
    {
        var root = Path.Combine(Path.GetTempPath(), $"va-root-{Guid.NewGuid():N}");
        try
        {
            var exeDir = Directory.CreateDirectory(Path.Combine(root, "launcher")).FullName;

            var paths = LauncherPaths.Resolve([], exeDir);
            paths.EnsureLayout();

            Assert.Equal(exeDir, paths.Root);
            Assert.True(Directory.Exists(Path.Combine(exeDir, "server")));
            Assert.True(Directory.Exists(Path.Combine(exeDir, "server_versions")));
            Assert.True(Directory.Exists(Path.Combine(exeDir, "admin_versions")));
            Assert.False(Directory.Exists(Path.Combine(exeDir, "admin")));
            Assert.True(Directory.Exists(Path.Combine(exeDir, "game_versions")));
            Assert.True(Directory.Exists(Path.Combine(exeDir, "replays")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GelistirmeDerlemesi_DeployLauncherKlasorunuBulur()
    {
        // dotnet run: the exe sits in bin\...; the repo's deploy\launcher\ is found by walking up.
        var repo = Path.Combine(Path.GetTempPath(), $"va-repo-{Guid.NewGuid():N}");
        try
        {
            var deployed = Path.Combine(repo, "deploy", "launcher");
            Directory.CreateDirectory(Path.Combine(deployed, "server"));
            var exeDir = Directory.CreateDirectory(Path.Combine(repo, "launcher", "bin", "Debug")).FullName;

            Assert.Equal(deployed, LauncherPaths.Resolve([], exeDir).Root);
        }
        finally
        {
            if (Directory.Exists(repo)) Directory.Delete(repo, recursive: true);
        }
    }

    [Theory]
    [InlineData("admin_versions")]
    [InlineData("server_versions")]
    [InlineData("admin")]
    public void KokTespiti_SurumKlasoruyleDeOlur(string folder)
    {
        // Legacy admin\ still identifies a root: an older deployment must keep opening in place.
        var repo = Path.Combine(Path.GetTempPath(), $"va-repo-{Guid.NewGuid():N}");
        try
        {
            var deployed = Path.Combine(repo, "deploy", "launcher");
            Directory.CreateDirectory(Path.Combine(deployed, folder));
            var exeDir = Directory.CreateDirectory(Path.Combine(repo, "launcher", "bin", "Debug")).FullName;

            Assert.Equal(deployed, LauncherPaths.Resolve([], exeDir).Root);
        }
        finally
        {
            if (Directory.Exists(repo)) Directory.Delete(repo, recursive: true);
        }
    }

    [Fact]
    public void KokArgumaniYoksaCozumDuser()
    {
        // Without --root the test host's own folder is used; only "something was resolved" matters.
        Assert.False(string.IsNullOrWhiteSpace(LauncherPaths.Resolve([]).Root));
    }

    [Fact]
    public void KisaltmaSonuKorur()
    {
        var shortened = LauncherPaths.Shorten(@"C:\cok\uzun\bir\yol\deploy\launcher", 12);

        Assert.Equal(12, shortened.Length);
        Assert.EndsWith("launcher", shortened);
    }
}
