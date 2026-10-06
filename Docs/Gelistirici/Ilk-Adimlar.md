---
title: İlk Adımlar
---

# İlk Adımlar

Sıfırdan çalışır duruma ~15 dakika. Quest gözlüğü **gerekmez** — masaüstünde test edebilirsin.

---

## 1. Kurulum (yeni bilgisayarda bir kez)

Araç zincirinin tamamı (Unity + CLI, Git LFS, .NET SDK, Defender dışlamaları, MCP kayıtları)
tek yerde: **[Ortam Kurulumu](Ortam-Kurulumu.md)**. Kurulum bitince buradan devam et.

---

## 2. Rolünü ve sunucunu seç

`Tools > VortexArena > Development > Dev` penceresini aç.

- **Rol:** `player` (VR oyuncusu) ya da `admin` (masaüstü gözlemci). Kısayol: **Ctrl+Alt+R**, ikisini
  çevirir.
- **Hedef:** sunucu adresi. Liste `dev-targets.json`'dan gelir (`Local`, `Keşif (beacon)`, örnek PC).

> ⚠️ **Rol ve IP'yi sahneye yazma.** Bu değerler `EditorPrefs`'te kişisel kalır — böylece rol
> değiştirmek hiçbir sahneyi ya da asset'i kirletmez ve commit'inde görünmez.
> Boot sahnesine `[SerializeField]` override koyma; `AppBoot`'ta böyle bir alan yoktur.

### Aynı PC'de player + admin birlikte (Multiplayer Play Mode)

Tek Play ile iki pencere: ana editör gözlükte **player**, sanal oyuncu masaüstünde **admin**.
İkisi de dev penceresinde seçili hedefe bağlanır (ör. `Local`).

1. Paketi kur: Package Manager > **Add package by name** → `com.unity.multiplayer.playmode`.
   Paket `Packages/manifest.json`'a girer ve commit'lenir.
2. `Window > Multiplayer Play Mode` → bir **sanal oyuncu** aç ve ona **`admin` tag'i** ver
   (tag adları `player` / `admin`; büyük/küçük harf önemsiz).
3. Ana editörde rolü `player` seç (dev penceresi) ve Play'e **bir kez** bas — sanal oyuncu da
   Play'e girer.

> ⚠️ **Rol farkı yalnız tag'le verilir.** `EditorPrefs` makine çapında paylaşılır, iki süreç de
> aynı rol seçimini okur; tag seçimin önüne geçer. Tag'siz süreç `EditorPrefs` seçimiyle kalır.

Admin süreci gözlüğü kapmaz: rol admin çözülünce XR bırakılır (`AdminXrRelease`), HMD player
sürecine kalır. Aynı sebeple admin Windows build'i de Link'teki gözlükte açılmaz.

Sanal oyuncu **tam bir editör değildir** — yalnız Play penceresi verir; dev penceresi ve diğer
araçlar ana editörde kalır.

### "Kalibrasyonu atla" — Link ile gözlüklü testte A/B jestini atlama

Quest Link ile gözlüklü test ediyorsan her Play'de iki zemin işaretine eğilip A/B almak zorunda
değilsin: dev penceresinde **Kalibrasyonu atla** anahtarını aç (varsayılan kapalı, `EditorPrefs`'te
kişisel kalır). Sahne açılışında rig kafası **A işaretinin üstüne**, A→B'ye bakar ve varsayılan boya
(1,80 m) göre oturur biçimde konur, ardından başlık kendini kalibre bildirir — yani savaş kapıları
(ateş, hasar, canlanma) açılır. Çapa oluşturulmaz ve **gözlüğün kayıtlı gerçek hizalaması
silinmez**. Sunucusuz sandbox kipiyle birlikte de çalışır; o zaman bildirim gitmez, yerleştirme ve
yürüme aynıdır.

Dev hizalama yürürlükteyken kumanda:

| Girdi | Ne yapar |
|---|---|
| **Sol çubuk** (yat) | Kafanın bakış yönüne göre yürü (ileri/geri/yana), ~2 m/s |
| **Sağ çubuk** (yat) | Kafanın etrafında **30°** adım dön (her itişte bir adım) |
| **Sol çubuğa kısa basış** | Boyu yeniden oturt (yalnız yükseklik) |
| **Sol çubuğa 1 sn basış** | A işaretine dön |

Gözlüğü ilk taktığın anda boy kendiliğinden yeniden oturtulur. ⚠️ **Sağ çubuğun BASIŞI** gizli IP
panelinindir, dev yolu ona dokunmaz.

Gerçek kalibrasyon akışını denemek istediğinde anahtarı kapatmak gerekmez: admin ekranından
**kalibrasyonu sıfırla** → dev hizalama düşer ve elle A/B kapısı açılır; **yeniden yükle** → dev
hizalama geri gelir. Aynı süreçte gerçekten A/B aldıysan dev dalı kendiliğinden susar.

> ⚠️ **Bu bir hizalama testi DEĞİLDİR.** Sanal alan fiziksel alanla örtüşmez: çapa, zemin sapması,
> takip bozulması ve A/B jestinin kendisi bununla doğrulanamaz; duvar çarpışması yoktur. Alan dışı
> ve engel karartmaları ile ateş kapıları kafa konumuna baktığı için **yürüyerek de tetiklenir** —
> kurtarma yolu A'ya dönmektir. Oturarak test edersen dünya ayakta görünür ama gövde izleme kendi
> zeminini varsaydığı için karşı taraf avatarını oturur pozda ve yüksekte görür. Birden çok oyuncu
> aynı A noktasında doğar, yakınlık uyarısı titreşir. Anahtar **APK'ya taşınmaz** — kodun tamamı
> `#if UNITY_EDITOR` içindedir.

---

## 3. Sunucusuz ilk test (en hızlı yol, sınırlı)

Dev penceresinde *Play başlangıcı* = **Açık sahneden**, bir arena sahnesi açıkken Play'e bas.
Sunucu yoksa bağlantı kurulmaz ama sahne koşar: silahını ve efektlerini test edebilirsin.

Bu kipte:
- ✅ Silah ateşler, efektler çalışır, HUD çizilir (mod katalogdaki ilkine düşer)
- ✅ Mod kuralları `ModeDefinition`'daki **önizleme** alanlarından okunur (telde kural yoksa
  devreye giren fallback)
- ❌ Maç yoktur: takım, faz, süre, skor limiti gelmez — bunları **yalnız sunucu** üretir
- ❌ Can/skor/ölüm yoktur (bunlar sunucu-otoriter)

Yani mod/takım/süre denemek istiyorsan bir sonraki adım zorunlu.

> Ağ çağrılarının hepsi bağlantı yokken sessizce no-op'tur — kodunun etrafına
> `if (bağlıysa)` yazmana gerek yok.

---

## 4. Gerçek maç (sunucu + ikinci istemci)

**a) Sunucuyu başlat** — elle, her zaman:

```bat
cd Server\VortexArena.Server.App
dotnet run
```

Sunucu hiçbir yerden otomatik başlatılmaz ve editör onu ne başlatır ne öldürür. Açılış
başlığında kayıtlı modları (`tdm, ffa`) ve harita tablosunu görürsün.

**b) Maçı başlatacak bir admin bağla.** Maçı yalnız admin rolündeki bir istemci başlatabilir:
ya `deploy\admin\VortexArena.exe`'yi çalıştır, ya da editörde rolü `Ctrl+Alt+R` ile `admin`
yapıp oradan başlat.

**c) Play'e bas.** Rolün `player` ise lobiye düşer, admin maçı başlatınca arenaya geçersin.

---

## 5. Değişikliğini doğrula

**Kural: doğrulamayı batch'le.** Her küçük düzenlemeden sonra build alma — tüm işi bitir, sonda
tek geçiş yap.

```bash
# Sunucu
cd Server && dotnet build          # 0 hata / 0 uyarı bekleriz

# Unity (editör açıkken)
unity cmd recompile
unity cmd recompile_status         # completed olana kadar
unity cmd get_console_logs --json  # 0 hata / 0 uyarı bekleriz
```

`unity` komutu Unity CLI'dır (`%LOCALAPPDATA%\Unity\bin`) ve editör açıkken ona bağlanır.

Oyuncu uygulaması **gözlükte** koşar, `Debug.Log` satırları da yalnız orada durur — editör konsolu
onları göstermez. Bu yüzden cihazdaki **uyarı ve hata** satırları kontrol kanalından sunucuya taşınır
(`client_log`, `Docs/ArenaNet-Protokol.md` §5.1): sunucu konsoluna basılır ve günlük dosyasına yazılır
(`Server/README.md`). Test kartlarında "gözlükten gelen satır" denince kastedilen orasıdır —
işletmede USB yoktur, cihaza bağlanarak log okumak bir çalışma biçimi değildir.

> ⚠️ Editör **açıkken** `unity build` / `unity test` çalıştırma — ayrı bir batch-mode editör
> başlatır ve proje kilidine takılır. In-editor `unity cmd build` / `run_tests` kullan.

---

## 6. Şimdi ne okumalı

| Sıradaki | Neden |
|---|---|
| **[Yemek Kitabı](Yemek-Kitabi.md)** | Günlük işin: silah, hasar, olay, HUD, mod, arena reçeteleri |
| [Yapma Listesi](Yapma-Listesi.md) | Bir şey "sessizce çalışmıyorsa" ilk bakılacak yer |
| [Sahne Kurulumu](Sahne-Kurulumu.md) | Yeni arena yapacaksan |

---

## Sık takılınan üç şey

**"Play'e bastım, hiçbir şey olmuyor."**
Boot sahnesinden mi başlıyorsun? Dev penceresinde *Play başlangıcı* ayarı var: Boot'tan ya da açık
sahneden. Açık sahneden başlarken `DevSession` yalnız **bağlanır**; arena/HUD dolu görünse de maç
verisi (takım, faz, süre, limit) sunucudan gelir — sunucuda koşan bir maç yoksa hiçbir şey olmaz,
maçı bir **admin** başlatmalıdır.

**"Silahım ateş ediyor ama karşı taraf can kaybetmiyor."**
Üç şeyi sırayla kontrol et: (1) `ArenaCombat.ReportHit`/`ReportRaycastHit` çağrılıyor mu,
(2) hedefte `RemoteHitBox` var mı, (3) sunucu konsolunda `hit_report reddedildi: <sebep>` satırı
var mı — sebep orada yazar (faz Live değil, hedef ölü, dost ateşi…).

**"Maç başlamıyor, sunucu bir şey demiyor."**
Sunucu konsolunda tek satırlık bir ret vardır: sahne adı `maps.json`'da yok, harita modu
desteklemiyor ya da sahne bir istemcinin build listesinde yok. En sık sebep:
**`Export Server Config` çalıştırılmamış.**
