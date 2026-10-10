# VortexArena.Launcher — operatör launcher'ı

**.NET 10 / WPF** Windows masaüstü uygulaması, **tek exe**. İşletmede operatörün açtığı tek program
budur: maç sunucusunu çalıştırır, yönetim uygulamasını başlatır, gözlüklere oyun sürümü kurar ve maç
kaydını açıp kapatır.

## Yerleşim — kök, exe'nin klasörüdür

Launcher ayarlardan yol sormaz; her şeyi **kök klasöre** göre bulur. Dağıtım paketi
`deploy\launcher\`'dır: tek exe, yanında dört klasör ve `platform-tools\`; sahaya olduğu gibi taşınır:

```
<kök>\VortexArena.Launcher.exe
<kök>\server\                             mekan VERİSİ: config\server.json, config\devices.json,
                                          logs\, ölçüm dosyaları
<kök>\server_versions\server_v<N>\VortexArena.Server.App.exe   (+ dll'ler, config\maps.json,
                                          config\server.json tohumu, firewall-kur.cmd)
<kök>\admin_versions\admin_v<N>\VortexArena.exe   (+ VortexArena_Data\, UnityPlayer.dll, …)
<kök>\game_versions\game_v<N>.apk
<kök>\replays\*.vxr
<kök>\platform-tools\adb.exe              (+ dll'ler; deploy betiği doldurur)
```

`server\` ile `server_versions\` ayrıdır: sunucu `--data-dir <kök>\server` ile başlatılır, yani
config'ini, günlüklerini ve ölçüm dosyalarını oraya yazar; `maps.json`'ı **yalnız** çalıştığı
sürümün `config\` klasöründen okur. İlk açılışta `<kök>\server\config\server.json` yoksa sürümün
içindeki kopyadan tohumlanır. Böylece sürüm değiştirmek operatör verisine dokunmaz.

Kök çözüm sırası:

1. `--root <klasör>` argümanı,
2. exe'nin klasöründe `server\`, `server_versions\` ya da `admin_versions\` varsa orası (eski
   paketlerdeki `admin\` da kök sayılır),
3. geliştirme kolaylığı (`dotnet run`): exe'den yukarı en çok 6 seviye çıkıp bu yerleşimi taşıyan
   ilk `deploy\launcher\`,
4. exe'nin klasörü.

Launcher açılışta kökte `server\`, `server_versions\`, `admin_versions\`, `game_versions\` ve
`replays\` klasörlerini **boş da olsa oluşturur**; sunucu ve yönetim build'leri kendi sayfalarındaki
sürüm listesinden indirilir. Build gelene kadar sayfa boş durumu yazar
(`Sunucu yok. Aşağıdaki listeden bir sürüm indirin.`); elle kopyalanan build launcher yeniden
açılmadan görülür.

Aynı anda **tek launcher** açılır. Başka bir klasördeki kopya açılmaz ve klasör de oluşturmaz;
açık olanın yolunu gösterip kapanır. Aynı exe'ye yeniden tıklamak yalnız açık pencereyi öne getirir.

> **Exe yolları ayar değildir.** Operatörün elle gösterdiği bir sunucu exe'si, başka bir işletmenin
> `config\maps.json`'unu taşıyan bir dağıtım olabilirdi; kök tek kaynaktır.

## Sayfalar

| Sayfa | Ne yapar |
|---|---|
| **Sunucu** | Mekan seçimi, başlat/durdur, canlı durum (sürüm, faz, mod, sahne, oyuncu/yönetim sayısı, çalışma süresi), sunucu sürümleri (indir/seç, *Güvenlik duvarı* düğmesi), günlük takibi |
| **Yönetim** | Yönetim uygulamasını `--server-ip`/`--server-port` ile başlatır, durdurur; yönetim sürümlerini listeler, indirir ve hangisinin başlatılacağını seçtirir |
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

Mekan listesi launcher'a gömülü değildir — **seçili sunucu sürümünün** `config\maps.json`'ından
okunur — yalnız o sürüm klasörünün kendi dosyası, sunucunun okuduğu yerle aynı; üst klasörlerde
aranmaz. Dosya yoksa liste boş kalır ve sayfa sebebini yazar. Sürüm seçimi değişince liste
yeniden okunur. Yeni işletme eklendiğinde Unity'de
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
silmez. Kurulum `adb install -r -g` ile yapılır; ardından `GamePackage.RuntimePermissions`
listesindeki her izin `adb shell pm grant` ile ayrıca verilir (uzamsal veri izni `USE_SCENE` —
olmadan kalibrasyon çapası cihaza kaydedilmez). İzin verilemezse kurulum yine başarılı sayılır,
operatöre uyarı düşer: oyun izni ilk açılışta gözlükte sorar.

Tek istisna `INSTALL_FAILED_UPDATE_INCOMPATIBLE` (aynı paket farklı imzayla kurulmuş): launcher
onay ister ve **yalnız o sürümün paketini** kaldırıp yeniden kurar.

Liste satırları uzak ∪ yerel ∪ gözlükte kurulu sürümlerin birleşimidir. Yerelde olup güncelleme
sunucusunda görünmeyen (ya da liste o an alınamayan) sürüm **üstü çizili bulut** ikonuyla işaretlenir.
Liste alınamazsa sayfanın üstünde uyarı bandı çıkar ve 30 saniyede bir yeniden denenir.

## Sürümlü build'ler: her sürüm kendi klasöründe

Sunucu ve Windows yönetim build'i aynı mekanizmayı paylaşır — tek fark paket adı, exe adı ve
klasörü:

| Build | Paket | Yerleşim | Sayfa |
|---|---|---|---|
| Sunucu | `server_v<N>.zip` | `<kök>\server_versions\server_v<N>\VortexArena.Server.App.exe` | Sunucu |
| Yönetim | `admin_v<N>.zip` | `<kök>\admin_versions\admin_v<N>\VortexArena.exe` | Yönetim |

Arşivin kökünde build'in kendisi durur (sunucuda dll'ler + `config\maps.json` + `config\server.json`
tohumu + `firewall-kur.cmd`; yönetimde `VortexArena_Data\`, `UnityPlayer.dll`, …).

İndirme sırası: `<ad>.zip.part` → ad değişimi `<ad>.zip` → `<ad>.extract\`'a açma → arşivde paketin
exe'si var mı kontrolü → klasörün `<ad>\` olarak taşınması → zip'in silinmesi. Yarım kalan bir
`<ad>\` **kurulu görünürdü** ve operatör bozuk bir build başlatırdı; bu yüzden taşıma en sonda
yapılır. İptal ya da hatada artıklar (`<ad>.zip`, `<ad>.extract\`) hemen silinir; kilitli olduğu
için silinemeyen (ve yarım kalan `.part`) bir sonraki liste yenilemesinde — hiçbir indirme
sürmüyorsa — süpürülür. Hiçbir adım başka bir sürümün klasörüne dokunmaz.

Başlatılan sürüm: `serverVersion` / `adminVersion` ayarı, o sürüm açılmışsa odur; değilse **açılmış
en yüksek sürüm**. Otomatik seçim kaydedilmez — kayda yalnız operatörün *Seç* dediği sürüm girer.
Build çalışırken seçim değiştirilemez. Liste satırları uzak ∪ açılmış sürümlerin birleşimidir;
yerelde olup sunucuda görünmeyen (ya da liste o an alınamayan) sürüm **üstü çizili bulut** ikonuyla
işaretlenir, liste alınamazsa uyarı bandı çıkar ve periyodik olarak yeniden denenir. Elle kopyalanan
ya da silinen klasör birkaç saniye içinde listeye yansır.

Sunucu tarafında seçili sürüm ayrıca şunları belirler: `config\maps.json`'dan okunan mekan listesi,
`config\server.json`'dan okunan kontrol portu tohumu, `--data-dir` dışındaki her yol ve
*Güvenlik duvarı* düğmesinin çalıştırdığı betik. Sürüm seçimi değişince hepsi yeniden okunur.

**Güvenlik duvarı:** her yeni exe yolu için Windows izni yeniden sorar, yani yeni bir sunucu
sürümünün ilk açılışında gözlükler sunucuyu bulamayabilir. Sunucu sürüm kartındaki *Güvenlik duvarı*
düğmesi seçili sürümün `firewall-kur.cmd` betiğini **yönetici olarak** çalıştırır; onay verilmezse
işlem iptal edilir ve sayfa "Yönetici onayı verilmedi." der. Sunucu sürümü yokken düğme pasiftir.
Kurulum biten sunucu sürümü için operatöre bu düğme hatırlatılır.

Önceki launcher oturumundan kalan yönetim uygulaması sahiplenilir: `admin_versions\admin_v<N>\`
altından başlamış her `VortexArena` süreci sayılır ve sürüm numarası yolundan okunur. Tek klasörlü
eski `admin\` yerleşimi **kullanılmaz** (yalnız kök tespitinde kabul edilir). Sunucu tarafında
"çalışıyor" kararı HTTP durumundan gelir; zorla kapatma `server_versions\server_v<N>\` altındaki
exe'lerin yanı sıra eski `server\VortexArena.Server.App.exe`'yi de bulur — açık kalan eski bir
sunucu kontrol portunu tutar ve yenisi hiç açılmaz.

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
| `controlPortOverride` | Kontrol portu; `0` = `server\config\server.json → controlPort`, o yoksa seçili sürümün `config\server.json`'u, o da yoksa `47821` |
| `versionsUrl` | Güncelleme sunucusunun sürüm listesi ucu |
| `downloadBaseUrl` | APK indirme kök adresi |
| `adminVersionsUrl` | Yönetim build'i sürüm listesi ucu |
| `adminDownloadBaseUrl` | Yönetim build'i (`admin_v<N>.zip`) indirme kök adresi |
| `adminVersion` | Başlatılacak yönetim sürümü; `0` = açılmış en yüksek sürüm |
| `serverVersionsUrl` | Sunucu build'i sürüm listesi ucu |
| `serverDownloadBaseUrl` | Sunucu build'i (`server_v<N>.zip`) indirme kök adresi |
| `serverVersion` | Başlatılacak sunucu sürümü; `0` = açılmış en yüksek sürüm |
| `preferredDeviceSerial` | Son seçilen gözlük; tekrar takıldığında yine seçilir |

Yönetim uygulamasının bağlandığı sunucu adresi **kaydedilmez**: her açılış `127.0.0.1` ile başlar,
*Yönetim > Gelişmiş*'te değiştirilen adres yalnız o oturumda geçerlidir. Kaydedilen uzak bir adres
her açılışta yönetimi sessizce başka bir makineye gönderirdi; eski dosyadaki `serverIp` anahtarı
yok sayılır.

`versionsUrl` / `downloadBaseUrl` ile sunucu ve yönetim karşılıkları ileride lisans backend'iyle
değişecek; bu yüzden liste kaynağı `IVersionSource` arayüzünün arkasındadır — oyuncu APK'sı, sunucu
ve yönetim build'i aynı sınıfın üç ayrı örneğidir, paket adlandırması (`GamePackage` /
`BuildPackage.Server` / `BuildPackage.Admin`) kurucuya geçirilir.

## Dosyalar

| Yer | Sorumluluk |
|---|---|
| `App.xaml(.cs)` | Uygulama kabuğu, tema birleştirme, tek örnek kilidi, yakalanmamış hata kutusu |
| `MainWindow.xaml(.cs)` | Kenar çubuğu + sayfa barındırma, koyu başlık çubuğu, çıkış onayı |
| `Views/` | Sayfa görünümleri (`ServerPage`, `AdminPage`, `VersionsPage`, `RecordingPage`) + `ExitDialog` + `BuildVersionRow.xaml` (sunucu ve yönetim sayfalarının ortak sürüm satırı) |
| `ViewModels/` | `MainViewModel` + sayfa başına bir view model + satır view model'leri; sürüm listesi kartı `BuildVersionListViewModel`'dir, sayfa onu `IBuildVersionListHost` olarak besler |
| `Services/` | `LauncherPaths`, `LauncherSettings`, `VenueCatalog`, `ServerController`, `ServerStatusClient`, `AdminController`, `LogTailer`, `AdbService`, `VersionSource`, `VersionCatalog`, `ReplayLibrary`, `GamePackage`, `BuildPackage`, `BuildVersionCatalog` |
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
(tek dosya, self-contained) + yanında boş `server\`, `server_versions\`, `admin_versions\`,
`game_versions\`, `replays\` ve repodan aynalanan `platform-tools\`. Betik yeniden koşunca exe'yi ve
`platform-tools\`'u yeniler; yerleşim klasörlerinin içeriğine dokunmaz, klasördeki diğer her şeyi
(eski çıktı) siler. Betik tek dosya anahtarını `-p:VortexSingleFile=true` ile açar;
`RuntimeIdentifier`/`SelfContained` csproj'da **koşulludur**, çünkü RID'siz test projesi
RID'li self-contained bir projeye referans veremez.

**Ön koşul:** .NET 10 SDK (`dotnet` PATH'te).
