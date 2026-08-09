# scripts/ — dağıtım ve doküman betikleri

Dört bileşenin her biri kendi betiğiyle `deploy/` altına üretilir. Betikler **idempotent**:
hedef klasörü silip yeniden yazarlar.

| Betik | Kaynak | Çıktı | Ön koşul |
|---|---|---|---|
| `deploy-admin-game.bat` | Unity projesi (`Assets/`) | `deploy\admin\VortexArena.exe` | Unity Editor kapalı (betik zorlamaz) |
| `deploy-player-apk.bat` | Unity projesi (`Assets/`) | `deploy\player\game.apk` + `install_game.bat`; sonda APK sunucudaki yayın ucuna POST'lanır (`updater_uploader/` — uç kapalıysa yalnız uyarı, build başarılı sayılır) | Unity Editor kapalı + **Android Build Support** modülü |
| `deploy-server.bat` | `Server/VortexArena.Server.App` | `deploy\server\VortexArena.Server.App.exe` | .NET 10 SDK |
| `deploy-launcher.bat` | `launcher/VortexArena.Launcher` (WPF) | `deploy\launcher\VortexArena.Launcher.exe` | .NET 10 SDK + launcher kapalı |
| `deploy_android_updater.bat` | `updater/` (Kotlin Android) | `deploy\updater\VortexUpdater.apk` + `install_updater.bat` | Unity'nin **Android Build Support** modülü (JDK/Gradle oradan — editör açık olabilir, Unity başlatılmaz); Android SDK'sı `%LOCALAPPDATA%\VortexUpdaterSdk` köküne ilk koşuda iner → ilk koşu internet ister |
| `docs-setup.bat` | — | `..\vortexarena-docs-site\` (repo DIŞI) | Node 22+, git, internet (yalnız kurulumda) |
| `defender-exclusions.cmd` | `defender-exclusions.ps1` | Windows Defender dışlama listesi | **Yönetici** + Defender'ın etkin olması |

İki Unity betiği aynı `PlayerBuildTool` sınıfını farklı `-executeMethod` ile çağırır ve **aynı
sahne listesini** kullanır (Build Settings). Fark yalnız platformdur: Windows = admin, Android =
Quest oyuncusu. Rol ve sunucu adresi **hiçbirine gömülmez** — admin adresi launcher'ın
`--server-ip` argümanından, oyuncu ise UDP beacon keşfinden alır, yani **aynı APK her gözlüğe**
kurulur.

## Dokümantasyon sitesi

`docs-setup.bat` **yeni bilgisayarda bir kez** çalıştırılır: Quartz'ı klonlar, npm bağımlılıklarını
kurar, `site\content → repo\Docs` junction'ını kurar ve VortexArena'ya özel `quartz.config.yaml`'i
yazar. Günlük kullanım repo kökündeki **`docs-serve.bat`** (→ http://localhost:1111).

- **İçerik repodadır** (`Docs/`), motor değildir. `node_modules` (~365 paket) repoya girseydi Unity
  onu import etmeye çalışır ve git geçmişi şişerdi — bu yüzden motor `..\vortexarena-docs-site`.
- Junction sayesinde `Docs/` altında bir `.md` kaydedildiği anda tarayıcı kendini yeniler.
- Yazı tipleri **local** (`fontOrigin: local`): internetsiz makinede de doğru görünür.
- `quartz.config.yaml` kuruluysa **korunur** — elle ayar yaptıysan `docs-setup.bat` ezmez.

## Defender dışlamaları (`defender-exclusions.cmd`)

**Yeni bilgisayarda bir kez**, sağ tık → *Yönetici olarak çalıştır*. Gerçek işi
`defender-exclusions.ps1` yapar; `.cmd` yalnız yönetici kontrolü + çift tıklanabilirlik içindir.

Neden: IL2CPP build'i on binlerce `.cpp`/`.obj` üretir, `Library/` sürekli yazılıp okunur, shader
varyantları paralel derlenir. Defender'ın gerçek zamanlı koruması **her dosya açılışında** araya
girer ve çok çekirdekli derlemenin önünde kuyruk oluşturur; proje büyüklüğüne göre build ve import
sürelerinde %20-40 bandında kazanç normaldir.

İki tür dışlama yazılır:

- **Yol** (`ExclusionPath`) — repo kökü (`Library/`, `Temp/`, `deploy/`, `Server/`, `launcher/`
  hepsi içinde), Unity Editor kurulumu, Unity/Hub cache'leri, `.gradle` · `.nuget` · `.dotnet` ·
  npm cache'leri, kuruluysa IDE klasörleri, kuruluysa `..\vortexarena-docs-site`
  (`node_modules`, ~365 paket).
- **İşlem** (`ExclusionProcess`) — build zincirinin exe'leri: `Unity.exe`, `bee_backend.exe`,
  `il2cpp.exe`, `cl.exe`, `link.exe`, `MSBuild.exe`, `dotnet.exe`, `java.exe`, `adb.exe`…

**Yollar elle yazılmaz, türetilir:** repo kökü betiğin bir üstüdür, Unity Editor kökü Hub'ın
ikincil kurulum yolundan (`%APPDATA%\UnityHub\secondaryInstallPath.json`) da okunur. Sadece
diskte **var olan** isteğe bağlı klasörler eklenir — kurulu olmayan bir programın klasörünü
dışlamak gereksiz bir açıklıktır. Başka bir Unity projesi de dışlanacaksa `-ExtraPath` ile geçilir.

```bat
defender-exclusions.cmd                              :: kur
defender-exclusions.cmd -List                        :: yalnız listele (hiçbir şey yazmaz)
defender-exclusions.cmd -Remove                      :: aynı listeyi geri al
defender-exclusions.cmd -ExtraPath D:\games\digerproje
```

İdempotent: var olan dışlama tekrar eklenmez (`= zaten var` satırı). **Üç varyant da yönetici
ister** — `-List` dahil: Defender yetkisiz oturumda listeyi vermez, yerine
`N/A: Must be an administrator to view exclusions` döndürür (betik bunu liste sanmaz, adıyla
söyler).

- ⚠️ **Dışlanan klasörler artık taranmıyor.** Oraya indirme yapma; asset store paketi ya da
  GitHub'dan çekilen arşiv önce başka bir yere inip kontrol edilir.
- ⚠️ **İşlem dışlaması yol dışlamasından daha geniş bir kapıdır** — o sürecin *dokunduğu her dosya*
  taranmaz. Listeye kendi başına exe ekleme; build zinciri dışına çıkarma.
- Makine kurumsal ilkeyle (Intune / grup ilkesi) yönetiliyorsa komut "erişim reddedildi" verir ve
  betik bunu adıyla söyler — dışlamalar merkezden tanımlanır, IT'ye danışılır. Defender pasif
  kipteyse (üçüncü parti antivirüs varsa) betik uyarır: kayıtlar yazılır ama build süresine etkisi
  olmaz, asıl dışlamalar o ürünün arayüzünden tanımlanmalıdır.
### ⚠️ Dışlamalar Smart App Control'ü susturmaz

**Smart App Control** (Akıllı Uygulama Denetimi) bir antivirüs ayarı değil, bir **Code Integrity
politikasıdır**: Defender'ın dışlama listesini hiç okumaz ve açıkken AV dışlamaları da dikkate
alınmaz. Yani bu betik hatasız koşsa bile SAC açık bir makinede ne uyarılar biter ne de hız
kazancı gelir.

Unity'de somut hâli: **Burst**, `Library/BurstCache/JIT/` altına çalışma anında **imzasız native
DLL** üretir ve onu yükler. SAC bu yüklemeyi engeller — `CodeIntegrity` olayı **3077** (audit
değil, blok), yanında `3118 Smart App Control Block Details`. `git pull` kodu/paketi değiştirdiğinde
Burst yeniden derler, yeni imzasız DLL doğar ve uyarı tekrarlar. Aynı duvar kendi imzasız
çıktılarımız için de geçerlidir (`deploy\admin\VortexArena.exe`, sunucu, launcher).

Betik bu durumu açılışta **kendisi uyarır**. Elle bakmak için:

```powershell
(Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy').VerifiedAndReputablePolicyState
:: 0 = kapalı · 1 = zorunlu · 2 = değerlendirme
Get-WinEvent -LogName Microsoft-Windows-CodeIntegrity/Operational -MaxEvents 20
```

Kapatma: *Windows Güvenliği → Uygulama ve tarayıcı denetimi → Akıllı Uygulama Denetimi → Kapalı.*
⚠️ **Tek yönlü kapı:** SAC bir kez kapatılınca Windows yeniden kurulmadan geri açılamaz.

> Teşhis sırası, bir uyarıyı "antivirüs" sanmadan önce: `Get-MpThreatDetection` **boşsa** olay AV
> değildir — `CodeIntegrity/Operational` log'una bak. Gerçek zamanlı korumayı kapatmak uyarıyı
> susturur ama sebebi gizler ve makineyi savunmasız bırakır.

- **Daha temiz alternatif: Dev Drive.** Windows 11 22H2+ ile gelen ReFS tabanlı birim, "performans
  modu"nda Defender'ı asenkron çalıştırır — dışlama listesi tutmaya gerek kalmaz.
  *Ayarlar → Sistem → Depolama → Gelişmiş depolama ayarları → Diskler ve birimler → Dev Drive
  oluştur.* En az 50 GB gerekir; Unity projesi + `Library/` + `deploy/` için 200-300 GB rahat eder.
  Yeni disk kurulurken yapılacak iş; repo taşındığında bu betik yeni kökü kendiliğinden bulur
  (yolu kendi konumundan türetiyor).

## Çalıştırma ve pencerenin kapanması

Betiklerin hepsi **çift tıklanabilir**: iş bitince (başarı ya da hata) pencere `pause` ile bekler,
çıktı okunabilir. Hata durumunda en sonda `=== BASARISIZ (cikis kodu N) ===` satırı basılır — uzun
build log'unda hatayı aramaya gerek yok.

Bekleme yalnız betik çift tıklanarak / `cmd /c betik.bat` diye başlatıldığında devreye girer
(`%cmdcmdline%` betiğin adını içeriyor mu diye bakılır); zaten açık bir konsoldan çalıştırıldığında
beklemez. Otomasyonda kapatmak için:

```bat
deploy-launcher.bat --no-pause
set VORTEX_NO_PAUSE=1 && deploy-launcher.bat
```

## Unity build'lerinde canlı ilerleme (`lib\watch-unity-build.ps1`)

Batch-mode Unity konsola **hiçbir şey yazmaz**; 20 dakika boş ekrana bakılıyor ve build takıldı mı
ilerliyor mu anlaşılmıyordu. Bu yüzden `deploy-admin-game.bat` ve `deploy-player-apk.bat` Unity'yi
doğrudan değil `scripts\lib\watch-unity-build.ps1` üzerinden çalıştırır. İzleyici aynı komut
satırını kurar, kendi log'unu (`deploy\admin-build.log` / `deploy\player-build.log`) Unity yazarken
paylaşımlı kipte okur ve tek satırlık durum gösterir:

```
  [04:12 / ~12:30] Scriptler derleniyor | %53 (1450/2714) | Csc Meta.XR.Editor.dll | log 2.4 MB | cpu +9.8 sn -
```

- **Aşama** log işaretlerinden çıkarılır (lisans → paketler → asset refresh → import → platform
  geçişi → script derleme → domain reload → player build → shader → IL2CPP → bitiriyor). Aşama
  değişince o anki satır sabitlenir, geçmiş kalır. Aşamalar geri gitmez: build aşamasına
  girildikten sonra hazırlığa dönülmez (yoksa geç gelen `[Licensing::]`/shader satırları durumu
  başa atıyordu).
- **Yüzde** Bee/Tundra sayacından gelir (`[1450/2714 …]`); 30 sn tazelenmezse gizlenir — biten
  DAG'ın `2714/2714`'ü sonraki aşamada yalan olur.
- **Hareketsizlik uyarısı:** log 3 dakika (`-StallSeconds`) büyümez **ve** build zinciri
  (`Unity`, `bee_backend`, `il2cpp`, `cl`, `link`…) CPU harcamazsa uyarı + son log satırı basılır.
  Genel `Unity` adı CPU toplamına bilerek katılmaz: başka bir editör açıkken onun CPU'su takılmış
  bir build'i çalışıyor gösterirdi.
- **Hata satırları anında ekrana düşer** (proje kilidi, `error CS…`, `Aborting batchmode`) —
  log'da aramaya gerek yok. `[PlayerBuildTool]` satırları da olduğu gibi basılır.
- **Süre referansı:** başarılı koşunun süresi log'un yanına (`<log>.last`) yazılır, sonraki koşuda
  başlıkta `~mm:ss` olarak gösterilir ("normalde bu kadar sürüyordu"). İki build'in referansı
  ayrıdır — APK build'i admin build'inden belirgin uzun sürer.
- **Hangi metot / hangi platform:** `-Method` çağrılacak `-executeMethod` girişini,
  `-UnityBuildTarget` ise Unity'nin **açılış platformunu** belirler. Platformu build metodunun
  içinden çevirmek işe yaramıyor: `SwitchActiveBuildTarget` domain reload tetikliyor ve çalışan
  `-executeMethod` yarıda kalıyor. Bu yüzden **her iki betik de hedefini açıkça geçer**
  (`Win64` / `Android`) — aktif platform zaten doğruysa bayrak etkisizdir, değilse geçiş
  güvenli yerde, açılışta olur.
- **Ctrl+C** iptalinde izleyici Unity sürecini de öldürür (yoksa proje kilidi arkada kalırdı).
- Çıkış kodu Unity'ninkidir; `.bat` başarısızlık dalını aynen çalıştırır. İzleyici dosyası yoksa
  betik eski davranışa (sessiz build) düşer, sadece uyarı basar.

**Bitmiş bir log'u incelemek** (hangi aşamada ne kadar satır harcanmış, hata var mıydı):

```bat
powershell -NoProfile -File scripts\lib\watch-unity-build.ps1 -ReplayLog deploy\admin-build.log
```

> Çıktı **ASCII**'dir (konsol kod sayfası Türkçe karakterleri bozuyor). İzleyiciye yeni metin
> eklerken şapkasız/noktasız harf kullan.

## Neden bu ön koşullar?

- **Editör kapalı olmalı:** iki Unity betiği de batch-mode Unity başlatır
  (`-batchmode -executeMethod VortexArena.Core.Editor.PlayerBuildTool.BuildWindowsAdmin` /
  `…BuildQuestPlayer`). Aynı proje editörde açıkken **proje kilidine** takılır. Ama betik bunu
  **kontrol etmez** (bilinçli): editör kapatıldıktan sonra bile AI motoru gibi alt süreçlerin
  `Unity.exe`'si arka planda yaşayabiliyor ve `tasklist` kontrolü yanlış alarm veriyordu. Build
  ilerlemiyorsa Ctrl+C ile iptal edip süreçleri kapatın, tekrar deneyin. Log dosyasını script
  her koşuda siler; **silinemezse uyarır**, çünkü o dosyayı hâlâ bir Unity süreci tutuyor demektir
  (kilit için tasklist'ten çok daha güvenilir bir işaret) ve basılan log satırları bayat olabilir.
- **Android Build Support:** `deploy-player-apk.bat` build'e girmeden önce editör klasöründe
  `Data\PlaybackEngines\AndroidPlayer` var mı diye bakar. Modül yoksa Unity platformu Android'e
  çeviremez ve **sessizce Windows'ta devam edip `.exe` üretirdi** — bu yüzden erken ve adıyla
  durdurulur. Kurulum: Unity Hub > Installs > sürüm > Add modules > Android Build Support
  (+ SDK/NDK + OpenJDK).
- **Platform değişen koşu uzun sürer.** Hedef sabit olduğu için geçiş yalnız aktif platform
  hedeften farklıysa olur; o koşu tam reimport demektir (texture'lar yeniden sıkıştırılır:
  Quest'te ASTC, Windows'ta DXT) — 20-40 dk. Sonraki koşular hızlıdır. Betikler platformu build
  sonunda **geri almaz**: geri almak ikinci bir tam reimport daha olurdu ve gerekmez, iki betik de
  kendi hedefini açılışta zorluyor.
- **İki build birbirinin cache'ini ısıtmaz.** `Library/` ortaktır ama içindeki şeritler platform
  başınadır: shader varyantları grafik API'sine göre anahtarlanır (admin d3d11, oyuncu vulkan),
  asset artifact'ları sıkıştırma biçimine göre (DXT / ASTC), script derleme çıktıları hedefe göre
  ayrı klasörlerde. Yani her platform kendi cache'ini bir kez ısıtır; **soğuk cache'te sürenin
  büyük kısmı shader varyantı derlemektir**. Buradan çıkan pratik sonuç: `Library/`'yi silme, ve
  aynı gün ikisi de gerekiyorsa **önce admin, sonra APK** al (APK platformu Android'de bırakır).
- **Çalışan exe çıktı klasörünü kilitler.** `deploy-server.bat` ve `deploy-launcher.bat` publish'e
  girmeden önce `tasklist` ile kendi exe'sini arar (`VortexArena.Server.App.exe` /
  `VortexArena.Launcher.exe`) ve çalışıyorsa adıyla durur — yoksa `rmdir` yarıda kalıp publish
  anlamsız bir dosya izni hatası verirdi.

## Betik yazarken üç tuzak (kanıtlanmış)

- **Blok içindeki `echo`'da parantez kaçırılmadan yazılamaz.** `if … ( … )` bloğunun içinde
  `echo … (%VA_RESULT%).` yazılırsa `)` bloğu erkenden kapatır, geriye kalan `.` ayrı bir komut
  sanılır → betik `. was unexpected at this time.` deyip **o satıra hiç girilmese bile** ölür
  (cmd bloğu çalıştırmadan önce tümüyle ayrıştırır). Blok içinde her zaman `^(` / `^)` kullan.
  Yaşanmış örnek: `docs-setup.bat` npm adımından sonra sessizce sonlanıyor, junction ve
  `quartz.config.yaml` hiç kurulmuyordu.
- **`call <araç>.bat` çağıran betiği de öldürebilir.** Sarmalayıcı `.bat` dosyaları sonlarında
  `… & exit_with_errorlevel.bat` gibi zincirler taşıyabiliyor ve bu zincir `call` ile girilen
  batch bağlamını komple sonlandırıyor → betik hiçbir şey yazmadan ölür, çift tıklanmışsa pencere
  anında kapanır. Böyle bir aracı **her zaman ayrı bir çocuk süreçte** çağır:
  `cmd /c call "<tam yol>" …` — ve yolu önce `where` ile tam yola çöz (PATH'ten tırnaklı çağrı
  aracın kendi kök dizinini yanlış çözebiliyor).
- **Betik-içi değişkenler `VA_` önekli olmalı.** Bu değişkenler çocuk süreçlere miras kalıyor;
  kısa genel adlar derleme zincirini kırıyor. Yaşanmış örnek: `set "RC=0"` → CMake `RC`'yi
  resource compiler sanıp *"Could not find the compiler specified in the environment variable
  RC: 0"* ile üretimi kırdı (aynı risk MSBuild için de var — ortam değişkenlerini global property
  olarak okur, Unity → IL2CPP → MSVC zinciri dahil). Yeni değişken eklerken öneki koru.

## Ortam değişkenleriyle yol geçersiz kılma

| Değişken | Ne için | Varsayılan |
|---|---|---|
| `UNITY_EXE` | Unity editör exe'si | `C:\Program Files\Unity\Hub\Editor\<ProjectVersion>\Editor\Unity.exe` |

Unity sürümü `ProjectSettings/ProjectVersion.txt`'ten okunur — sürüm yükseltilince betik
kendiliğinden doğru editörü bulur.

## Sıra önemli mi?

Bağımsızlar, ayrı ayrı çalıştırılabilir. Ama **silah/harita SO'su değiştiyse** önce Unity'de
`Tools > VortexArena > Server > Export Server Config`, sonra `deploy-server.bat` (config'i o kopyalar).

Çıktıların işletmeye taşınma sırası: `deploy/README.md`.
