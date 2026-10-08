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
