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
            Assert.Equal(Path.Combine(root, "server", "VortexArena.Server.App.exe"), paths.ServerExe);
            Assert.Equal(Path.Combine(root, "admin", "VortexArena.exe"), paths.AdminExe);
            Assert.Equal(Path.Combine(root, "game_versions"), paths.GameVersionsDir);
            Assert.Equal(Path.Combine(root, "replays"), paths.ReplaysDir);
            Assert.Equal(Path.Combine(root, "server", "config", "server.json"), paths.ServerConfigJson);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void KokExeKlasorudur_UstKlasordekiYerlesimeBakilmaz()
    {
        // Shipped layout: deploy\launcher\ holds the exe AND server\, admin\, game_versions\, replays\.
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
            Assert.True(Directory.Exists(Path.Combine(exeDir, "admin")));
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
