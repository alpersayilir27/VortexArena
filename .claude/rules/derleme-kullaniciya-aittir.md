# Kural: Derleme/build kullanıcıya aittir — ajan projeyi DERLEMEZ

Bu projede **Unity derlemesini, build'ini ve testini ajan çalıştırmaz.** İş bitince ajan yalnız
*"bitti, doğrulanacak"* der; derlemeyi **kullanıcı** gerekirse kendisi yapar.

## Ne yapılmaz

Aşağıdakiler ajan tarafından **hiçbir basamakta** çağrılmaz — MCP'den de, shell'den de:

| Yasak | MCP karşılığı | Shell karşılığı |
|---|---|---|
| Script derlemesi | `recompile`, `recompile_status` | `unity cmd recompile` |
| Oyun build'i | `build`, `build_status`, `switch_build_target` | `unity build`, `scripts\deploy-*.bat` |
| Test koşusu | `run_tests`, `test_status` | `unity cmd run_tests` |
| Oynatma kipi | `editor_play`, `editor_pause`, `editor_stop` | — |
| Sunucu/launcher derlemesi | — | `dotnet build`, `dotnet publish`, `dotnet run` |
| Asset yeniden import | `refresh_unity`, `import_asset` | — |

⚠️ **"Sadece kontrol etmek için" istisnası YOKTUR.** Tek bir `recompile` bile editörü kilitler,
domain reload tetikler ve kullanıcının o an elinde tuttuğu sahne/Play oturumunu bozar. Ajan
"hızlıca teyit edeyim" diye bunu yaptığında maliyeti ajan değil kullanıcı öder.

## Bunun yerine

- İşi bitir, **ne değiştiğini ve neyin doğrulanması gerektiğini** yaz: "şu 3 dosya değişti,
  derleme gerekiyor" gibi tek satır yeter.
- Derleme gerçekten gerekiyorsa **kullanıcıdan iste**, kendin koşma. Kullanıcı isterse
  komutu `! <komut>` ile kendi oturumunda çalıştırır.
- **Kod doğruluğu yine ajanın sorumluluğudur** — derleyici olmadan da tutarlı yazılır: imzalar,
  namespace'ler, asmdef bağımlılıkları ve kullanılan API'ler yazmadan önce Read/Grep ile teyit
  edilir. "Derleme nasılsa yakalar" bir çalışma biçimi değildir.
- Konsol logu **okumak** (`get_console_logs` / `read_console`) yasak değildir: hiçbir şeyi
  tetiklemez, kullanıcı derledikten sonra sonucu okumak için doğru araçtır.

## Kullanıcı açıkça isterse

"Derle", "build al", "testleri koştur" gibi **açık** bir talimat geldiğinde kural düşer ve iş
[[unity-mcp-first]] basamaklarıyla yapılır. Kapalı/ima yollu okuma yapılmaz — "çalışıyor mu bak"
derleme izni DEĞİLDİR.

- Toplu doğrulama kuralı ([[batch-build-verification]]) bu kuralın altında okunur: batch'lenen
  doğrulama geçişini **kullanıcı** koşar, ajan yalnız ona kadar biriktirir.
- Araçların ne yaptığı ve nasıl çağrıldığı yine [[unity-cli]] / [[unity-mcp-first]] içindedir;
  buradaki kural **kimin çağıracağıdır**.
