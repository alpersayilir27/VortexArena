# VortexArena.Launcher — operatör launcher'ı

**.NET 10 / WPF** Windows masaüstü uygulaması. İşletmede operatörün açtığı tek program budur.
İki iş yapar: **sunucuyu doğru mekanla** ve **yönetim oyununu doğru adresle** başlatmak.

```
launcher'da mekan seç ──►  VortexArena.Server.App.exe --venue <mekan>
                                │
                                └─ o oturumda YALNIZ o işletmenin haritaları oynatılabilir;
                                   açılış sahnesi o mekanın lobisidir

launcher'da IP yaz    ──►  VortexArena.exe --server-ip <ip> --server-port <port>
                                │
                                └─ AppBoot argümanı okur → AppSession → admin gözlemci
                                   doğrudan bağlanır (IP SORULMAZ)
```

## Ne yapar / ne yapmaz

| Yapar | Yapmaz |
|---|---|
| Sunucuyu **mekan seçilmeden başlatmaz** (`--venue`) | Sunucuyu yönetmez/kapatmaz — kendi penceresinde Ctrl+C |
| Mekan listesini sunucunun `config\maps.json`'undan okur | İkinci bir mekan kataloğu tutmaz |
| Admin exe yolunu, IP/port'u ve mekanı kalıcı saklar | Maç yönetmez (mod/harita/start oyunun panelinde) |
| Oyunu `--server-ip`/`--server-port` ile başlatır | Başlattığı süreçlerle ağ üzerinden konuşmaz (protokole hiç girmez) |
| Başlatılan süreçleri izler (PID, çıkış kodu); oyunu durdurabilir | APK dağıtmaz |

> **Sunucuyu başlatmak isteğe bağlıdır.** Sunucu exe alanı boş bırakılırsa launcher yalnız yönetim
> oyununu başlatır ve sunucu eskiden olduğu gibi elle çalıştırılır. Launcher'dan başlatılması
> sadece `--venue`'nun **her seferinde** geçmesini garantiler.

## Neden mekan zorunlu

Sunucu mekan verilmeden açılırsa sırayla şuna bakar: `--venue` → `server.json → venue` → tek mekan
varsa o → konsolda sor. **Konsol etkileşimli değilse** (betik, servis, launcher) soru sorulamaz ve
**alfabetik ilk mekan** sessizce açılır. Operatör bunu fark etmez; yanlış işletmenin arenalarını
yönetmeye çalışır. Launcher bu yolu hiç bırakmaz: mekan seçilmeden **Sunucuyu Başlat** çalışmaz.

Mekan listesi launcher'a gömülü değildir — sunucu exe'sinin yanındaki `config\maps.json`'dan
okunur (exe klasöründen başlayıp yukarı 6 seviye aranır). Yeni işletme eklendiğinde Unity'de
`Tools > VortexArena > Server > Export Server Config` çalıştırmak yeter, launcher'da yapılacak iş yoktur.
Lobisi olmayan bir mekan listede **kırmızı** görünür ve başlatılmaz: sunucu o mekanda açık sahne
çözemeyip çıkış kodu 2 ile kapanırdı.

## Dosyalar

| Dosya | Sorumluluk |
|---|---|
| `VortexArena.Launcher/App.xaml(.cs)` | Uygulama kabuğu + tema birleştirme + yakalanmamış hata kutusu |
| `VortexArena.Launcher/MainWindow.xaml(.cs)` | Tek ekran: 1 · Sunucu / 2 · Bağlantı / 3 · Yönetim oyunu |
| `VortexArena.Launcher/LauncherConfig.cs` | Kalıcı ayarlar + doğrulama + `GameArguments`/`ServerArguments` |
| `VortexArena.Launcher/VenueCatalog.cs` | `maps.json` → mekan listesi (harita sayısı, lobi var mı) |
| `VortexArena.Launcher/Theme/Dark.xaml` | Karanlık tema paleti + kontrol şablonları |
| `VortexArena.Launcher.Tests/` | Argüman sözleşmesi, doğrulama, `maps.json` ayrıştırma testleri |

> **Argüman adları sözleşmedir.** `--server-ip`/`--server-port` Unity'deki
> `AppBoot.ArgServerIp`/`ArgServerPort` ile, `--venue` ise sunucudaki `Program.SelectVenue` ile
> birebir aynı olmalıdır. Üçü de testte doğrulanır — birini değiştirirsen **iki tarafı birlikte**
> değiştir.

> **Dış UI paketi yoktur** (MaterialDesignInXamlToolkit vb.). Tema `Theme/Dark.xaml` içinde elle
> yazılmıştır; sebep işletmede çoğu zaman internetsiz makinede derlenmesi — NuGet'ten çekilen bir
> tema kütüphanesi dağıtım betiğini ağa bağımlı hâle getirirdi.

## Geliştirme

```powershell
cd launcher
dotnet build VortexArena.Launcher.sln
dotnet test  VortexArena.Launcher.sln
dotnet run --project VortexArena.Launcher
```

Dağıtım build'i: repo kökünden `scripts\deploy-launcher.bat` → `deploy\launcher\`
(self-contained; operatör PC'sine .NET kurmak gerekmez).

## Ön koşullar

**.NET 10 SDK** (`dotnet` PATH'te) — tek ön koşul budur.

## Ayarlar nerede saklanıyor?

`%APPDATA%\VortexArena\launcher\settings.json` — kullanıcı profilinde, launcher klasörünün yanında
DEĞİL: `deploy-launcher.bat` çıktı klasörünü silip yeniden ürettiği için oradaki bir dosya her
dağıtımda kaybolurdu. Anahtarlar: `adminExePath`, `serverExePath`, `serverIp`, `serverPort`,
`venue`.
