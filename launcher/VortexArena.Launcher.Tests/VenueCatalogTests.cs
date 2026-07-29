using VortexArena.Launcher;
using Xunit;

namespace VortexArena.Launcher.Tests;

/// <summary>
/// <c>maps.json</c> ayrıştırma testleri. Ayrıştırma sunucudaki <c>MapTable</c> ile aynı iki kuralı
/// uygular: lobi = <c>modes</c> tam olarak <c>["lobby"]</c> (sahne adına BAKILMAZ), boş
/// <c>venue</c> = <c>Standard</c>.
/// </summary>
public class VenueCatalogTests
{
    private const string SampleJson = """
    {
      "maps": [
        { "sceneName": "Arena12x12",        "venue": "Outdoor12x12", "modes": ["ffa", "tdm"] },
        { "sceneName": "ArenaVortexAntep",  "venue": "VortexAntep",  "modes": ["ffa", "tdm"] },
        { "sceneName": "IceWorld",          "venue": "Outdoor12x12", "modes": ["ffa", "tdm"] },
        { "sceneName": "Lobby12x12",        "venue": "Outdoor12x12", "modes": ["lobby"] },
        { "sceneName": "LobbyVortexAntep",  "venue": "VortexAntep",  "modes": ["lobby"] }
      ]
    }
    """;

    [Fact]
    public void MekanlariAdaGoreSiraliDoner()
    {
        var catalog = VenueCatalog.FromJson(SampleJson, null);

        Assert.Null(catalog.Problem);
        Assert.Equal(["Outdoor12x12", "VortexAntep"], catalog.Names);
    }

    [Fact]
    public void HaritaSayisiMekanBasinaSayilir()
    {
        var catalog = VenueCatalog.FromJson(SampleJson, null);

        Assert.Equal(3, catalog.Venues[0].MapCount);
        Assert.Equal(2, catalog.Venues[1].MapCount);
    }

    [Fact]
    public void LobiIsaretiModlardanGelir()
    {
        var catalog = VenueCatalog.FromJson(SampleJson, null);
        Assert.All(catalog.Venues, v => Assert.True(v.HasLobby));
    }

    [Fact]
    public void LobisizMekanIsaretlenir()
    {
        const string json = """
        { "maps": [ { "sceneName": "A", "venue": "Yeni", "modes": ["tdm"] } ] }
        """;

        var catalog = VenueCatalog.FromJson(json, null);

        Assert.False(catalog.Venues[0].HasLobby);
        Assert.Contains("LOBİ YOK", catalog.Venues[0].Summary);
    }

    [Fact]
    public void SahneAdiLobiyleBaslasaBileModlarBelirler()
    {
        // "Lobby..." adı taşıyan ama modu lobi olmayan harita lobi SAYILMAZ — sunucudaki kuralın
        // aynısı (MapTable.ResolveLobbyScene sahne adına bakmaz).
        const string json = """
        { "maps": [ { "sceneName": "LobbyGorunumlu", "venue": "X", "modes": ["lobby", "tdm"] } ] }
        """;

        Assert.False(VenueCatalog.FromJson(json, null).Venues[0].HasLobby);
    }

    [Fact]
    public void BosVenueAlaniStandardSayilir()
    {
        const string json = """
        { "maps": [ { "sceneName": "Eski", "modes": ["tdm"] } ] }
        """;

        Assert.Equal([VenueCatalog.DefaultVenue], VenueCatalog.FromJson(json, null).Names);
    }

    [Fact]
    public void BozukJsonSebepDoner()
    {
        var catalog = VenueCatalog.FromJson("{ bu json degil", null);

        Assert.Empty(catalog.Venues);
        Assert.NotNull(catalog.Problem);
    }

    [Fact]
    public void MapsDizisiYoksaSebepDoner()
    {
        var catalog = VenueCatalog.FromJson("""{ "baska": 1 }""", null);

        Assert.Empty(catalog.Venues);
        Assert.Contains("maps", catalog.Problem);
    }

    [Fact]
    public void MapsJsonAramasi_OlmayanYoldaNullDoner()
    {
        Assert.Null(VenueCatalog.FindMapsJson(@"C:\olmayan\klasor\VortexArena.Server.App.exe"));
    }

    [Fact]
    public void SunucuExeYoksaSebepDoner()
    {
        var catalog = VenueCatalog.ForServerExe("");

        Assert.Empty(catalog.Venues);
        Assert.NotNull(catalog.Problem);
    }
}
