# VortexArena.Launcher — operatör launcher'ı

**.NET 10 / WPF** Windows masaüstü uygulaması, **tek exe**. İşletmede operatörün açtığı tek program
budur: maç sunucusunu çalıştırır, yönetim uygulamasını başlatır, gözlüklere oyun sürümü kurar ve maç
kaydını açıp kapatır.

## Yerleşim — kök, exe'nin klasörüdür

Launcher ayarlardan yol sormaz; her şeyi **kök klasöre** göre bulur. Dağıtım paketi
`deploy\launcher\`'dır: tek exe, yanında dört klasör ve `platform-tools\`; sahaya olduğu gibi taşınır:

```
<kök>\VortexArena.Launcher.exe
<kök>\server\VortexArena.Server.App.exe   (+ config\, logs\)
<kök>\admin\VortexArena.exe
<kök>\game_versions\game_v<N>.apk
<kök>\replays\*.vxr
<kök>\platform-tools\adb.exe              (+ dll'ler; deploy betiği doldurur)
```

Kök çözüm sırası:

1. `--root <klasör>` argümanı,
2. exe'nin klasöründe `server\` ya da `admin\` varsa orası,
3. geliştirme kolaylığı (`dotnet run`): exe'den yukarı en çok 6 seviye çıkıp bu yerleşimi taşıyan
   ilk `deploy\launcher\`,
4. exe'nin klasörü.

Launcher açılışta kökte `server\`, `admin\`, `game_versions\` ve `replays\` klasörlerini **boş da
olsa oluşturur**; build'ler sonradan içine kopyalanır. Build gelene kadar sayfa neyin nereye
konacağını yazar (`Sunucu yok. … şu klasöre koyun: <yol>`); kopyalanan build launcher yeniden
açılmadan görülür.

Aynı anda **tek launcher** açılır. Başka bir klasördeki kopya açılmaz ve klasör de oluşturmaz;
açık olanın yolunu gösterip kapanır. Aynı exe'ye yeniden tıklamak yalnız açık pencereyi öne getirir.

> **Exe yolları ayar değildir.** Operatörün elle gösterdiği bir sunucu exe'si, başka bir işletmenin
> `config\maps.json`'unu taşıyan bir dağıtım olabilirdi; kök tek kaynaktır.

## Sayfalar

| Sayfa | Ne yapar |
|---|---|
| **Sunucu** | Mekan seçimi, başlat/durdur, canlı durum (faz, mod, sahne, oyuncu/yönetim sayısı, çalışma süresi), günlük takibi |
| **Yönetim** | Yönetim uygulamasını `--server-ip`/`--server-port` ile başlatır, durdurur |
| **Versiyonlar** | Güncelleme sunucusundaki APK listesi, indirme, `adb` ile gözlüğe kurma, "Yüklü" rozeti |
| **Kayıt** | Maç kaydını açıp kapatır; `.vxr` dosyalarını listeler, yönetim uygulamasında oynatır |

Kenar çubuğundaki işaretler: sunucu/yönetim çalışıyorsa yeşil play üçgeni, başlıyor/kapanıyorsa sarı
nokta, kayıt dosyası açıkken yanıp sönen kırmızı nokta, kayıt açık ama lobide beklerken içi boş
kırmızı halka, yetkili gözlük bağlıyken gözlük işareti.

## Neden mekan zorunlu

Sunucu mekan verilmeden açılırsa sırayla şuna bakar: `--venue` → `server.json → venue` → tek mekan
varsa o → konsolda sor. **Konsol etkileşimli değilse** (betik, servis, launcher) soru sorulamaz ve
**alfabetik ilk mekan** sessizce açılır. Operatör bunu fark etmez; yanlış işletmenin arenalarını
yönetmeye çalışır. Launcher bu yolu hiç bırakmaz: mekan seçilmeden sunucu başlatılmaz.

Mekan listesi launcher'a gömülü değildir — `server\config\maps.json`'dan okunur (sunucu exe
klasöründen başlayıp yukarı 6 seviye aranır). Yeni işletme eklendiğinde Unity'de
`Tools > VortexArena > Server > Export Server Config` çalıştırmak yeter. Lobisi olmayan mekan
listede uyarıyla görünür: sunucu o mekanda açık sahne çözemeyip **çıkış kodu 2** ile kapanır.

## Sunucu kontrolü HTTP iledir, süreç takibiyle değil

Launcher sunucuyla aynı PC'de durur ve kontrol portundaki loopback uçlarını kullanır:
`GET /launcher/status`, `POST /launcher/recording`, `POST /launcher/shutdown`. Uçların sözleşmesi ve
`LauncherStatus` alanları **tek doğruluk kaynağında**: `Docs/ArenaNet-Protokol.md`, "Launcher
kontrol uçları" bölümü. DTO'lar paylaşılan `Assets/_Shared/Net/Protocol/LauncherApi.cs`'ten
link'lenerek derlenir — launcher ikinci bir kopya tutmaz.

- **"Çalışıyor" kararı `status` cevabıdır.** Launcher'dan önce ya da başka bir launcher örneğinden
  başlatılmış sunucu da böylece görünür ve durdurulabilir.
- Uç `404` dönerse sunucu eski sürümdür: sayfa "uyumsuz (eski sürüm)" der ve durdurmayı süreç
  üzerinden yapar.
- Durdurma önce temiz kapanıştır; 10 saniyede inmezse süreç ağacı öldürülür ve bu operatöre söylenir
  (açık kayıt dosyası yarım kalabilir).
- Sunucu başlatılırken **stdout yönlendirilmez**: launcher kapanınca kırık pipe sunucuyu düşürürdü.
  Operatörün penceresi günlük dosyasıdır (`server\logs\server-*.log`, artımlı okunur).

## Versiyonlar: paket adı sürüm başınadır

APK adı `game_v<N>.apk`, Android paket adı `com.vortex.arenav<N>` (kaynak: Unity'deki
`PlayerBuildTool`). Sürümler gözlükte **yan yana** yaşar; bu yüzden kurulum başka hiçbir sürümü
silmez. Kurulum `adb install -r -g` ile yapılır.

Tek istisna `INSTALL_FAILED_UPDATE_INCOMPATIBLE` (aynı paket farklı imzayla kurulmuş): launcher
onay ister ve **yalnız o sürümün paketini** kaldırıp yeniden kurar.

Liste satırları uzak ∪ yerel ∪ gözlükte kurulu sürümlerin birleşimidir. Yerelde olup güncelleme
sunucusunda görünmeyen (ya da liste o an alınamayan) sürüm **üstü çizili bulut** ikonuyla işaretlenir.
Liste alınamazsa sayfanın üstünde uyarı bandı çıkar ve 30 saniyede bir yeniden denenir.

## adb paketteki `platform-tools\`'tan çalışır

Launcher adb'yi kökteki `platform-tools\adb.exe`'den çalıştırır; operatör Android SDK kurmaz.
Kaynağı repo kökündeki `platform-tools/`'dur (`adb.exe`, `AdbWinApi.dll`, `AdbWinUsbApi.dll`,
`libwinpthread-1.dll`, `NOTICE.txt`, `source.properties`); `scripts\deploy-launcher.bat` onu her
koşuda pakete aynalar. adb exe'ye **gömülmez**: repoda tek kopya durur ve paketteki klasör launcher'ın
gerçekten kullandığı kopyadır. adb sürümünü yükseltmek = repo kökündeki dosyaları değiştirmek.

- Klasör yoksa *Versiyonlar* sayfası nereye konacağını yazar; kopyalanan klasör launcher yeniden
  açılmadan görülür. Launcher açılışta bu klasörü **oluşturmaz** — boş bir `platform-tools\` işe yaramaz.
- Çalışan adb sunucusu `adb.exe`'yi kilitler: betik kopyalamadan önce yalnız paketin
  `platform-tools\` klasöründen başlamış adb'yi kapatır; sonraki adb çağrısı onu yeniden başlatır.
- `.exe`/`.dll` Git LFS'tedir. LFS çekilmemiş klonda `adb.exe` ~130 baytlık işaretçidir ve ancak
  sahada ilk gözlük takılınca patlar; betik bunu görüp durur (`git lfs pull`).

Android platform-tools **Apache License 2.0** altındadır; `NOTICE.txt` klasörle birlikte dağıtılır.
Launcher kapanırken `adb kill-server` **yapılmaz** — makinedeki başka araçlar aynı sunucuyu
kullanıyor olabilir.

## Ayarlar

`%APPDATA%\VortexArena\launcher\settings.json` — kullanıcı profilinde, exe'nin yanında DEĞİL: exe
yeniden üretildiğinde oradaki dosya kaybolurdu. Bilinmeyen anahtarlar yok sayılır (eski dosyalar
sorun çıkarmaz).

| Anahtar | Anlam |
|---|---|
| `venue` | Sunucuya `--venue` olarak geçen mekan |
| `controlPortOverride` | Kontrol portu; `0` = `server\config\server.json → controlPort`, o da yoksa `47821` |
| `versionsUrl` | Güncelleme sunucusunun sürüm listesi ucu |
| `downloadBaseUrl` | APK indirme kök adresi |
| `preferredDeviceSerial` | Son seçilen gözlük; tekrar takıldığında yine seçilir |

Yönetim uygulamasının bağlandığı sunucu adresi **kaydedilmez**: her açılış `127.0.0.1` ile başlar,
*Yönetim > Gelişmiş*'te değiştirilen adres yalnız o oturumda geçerlidir. Kaydedilen uzak bir adres
her açılışta yönetimi sessizce başka bir makineye gönderirdi; eski dosyadaki `serverIp` anahtarı
yok sayılır.

`versionsUrl` / `downloadBaseUrl` ileride lisans backend'iyle değişecek; bu yüzden liste kaynağı
`IVersionSource` arayüzünün arkasındadır.

## Dosyalar

| Yer | Sorumluluk |
|---|---|
| `App.xaml(.cs)` | Uygulama kabuğu, tema birleştirme, tek örnek kilidi, yakalanmamış hata kutusu |
| `MainWindow.xaml(.cs)` | Kenar çubuğu + sayfa barındırma, koyu başlık çubuğu, çıkış onayı |
| `Views/` | Sayfa görünümleri (`ServerPage`, `AdminPage`, `VersionsPage`, `RecordingPage`) + `ExitDialog` |
| `ViewModels/` | `MainViewModel` + sayfa başına bir view model + satır view model'leri |
| `Services/` | `LauncherPaths`, `LauncherSettings`, `VenueCatalog`, `ServerController`, `ServerStatusClient`, `AdminController`, `LogTailer`, `AdbService`, `VersionSource`, `VersionCatalog`, `ReplayLibrary`, `GamePackage` |
| `Infrastructure/` | Elle yazılmış MVVM parçaları (`ObservableObject`, `RelayCommand`), dönüştürücüler, tek örnek kilidi, Win32 çağrıları |
| `Theme/Dark.xaml` · `Theme/Icons.xaml` | Palet + kontrol şablonları; ikon geometrileri |
| `../../platform-tools/` | Pakete kopyalanan adb dosyaları (repo kökü) |
| `../VortexArena.Launcher.Tests/` | Argüman sözleşmesi, `maps.json`/sürüm listesi/`adb` çıktısı ayrıştırma, birleştirme ve rozet kararı testleri |

> **Argüman adları sözleşmedir.** `--server-ip`/`--server-port`/`--replay` Unity'deki `AppBoot` ile,
> `--venue`/`--replay-dir` sunucuyla birebir aynı olmalıdır. Hepsi testte doğrulanır — birini
> değiştirirsen **iki tarafı birlikte** değiştir.

> **Dış UI paketi ve NuGet bağımlılığı yoktur** (MaterialDesignInXamlToolkit, CommunityToolkit.Mvvm
> vb.). Tema ve MVVM parçaları repoda elle yazılmıştır; sebep işletmede çoğu zaman internetsiz
> makinede derlenmesi.

> **İkonlar font glifi değil, `Geometry`dir.** "Segoe Fluent Icons" eski Windows'ta yoktur ve MDL2
> yedeği her kod noktasını taşımaz; eksik glif kutu olarak çizilir.

## Derleme ve dağıtım

```powershell
cd launcher
dotnet build VortexArena.Launcher.sln
dotnet test  VortexArena.Launcher.sln
dotnet run --project VortexArena.Launcher          # kök: deploy\launcher\ aranır
```

Dağıtım: repo kökünden `scripts\deploy-launcher.bat` → `deploy\launcher\VortexArena.Launcher.exe`
(tek dosya, self-contained) + yanında boş `server\`, `admin\`, `game_versions\`, `replays\` ve
repodan aynalanan `platform-tools\`. Betik yeniden koşunca exe'yi ve `platform-tools\`'u yeniler;
dört klasörün içeriğine dokunmaz, klasördeki diğer her şeyi (eski çıktı) siler. Betik tek dosya anahtarını `-p:VortexSingleFile=true` ile açar;
`RuntimeIdentifier`/`SelfContained` csproj'da **koşulludur**, çünkü RID'siz test projesi
RID'li self-contained bir projeye referans veremez.

**Ön koşul:** .NET 10 SDK (`dotnet` PATH'te).
