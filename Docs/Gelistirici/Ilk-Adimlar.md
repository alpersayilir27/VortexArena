---
title: İlk Adımlar
---

# İlk Adımlar

Sıfırdan çalışır duruma ~15 dakika. Quest gözlüğü **gerekmez** — masaüstünde test edebilirsin.

---

## 1. Kurulum (yeni bilgisayarda bir kez)

| # | Ne | Not |
|---|---|---|
| 1 | **Unity Hub + Editor 6000.3.20f1** | Android Build Support + SDK/NDK + OpenJDK modülleriyle |
| 2 | **Git + Git LFS** | `git lfs install` — repo LFS kullanıyor |
| 3 | Projeyi Unity'de bir kez aç | UPM paketleri manifest'ten iner |
| 4 | **.NET 10 SDK** | Sunucuyu derlemek için |
| 5 | `scripts\docs-setup.bat` | Bu dokümanı yerel olarak sunmak için (bir kez) |
| 6 | `scripts\defender-exclusions.cmd` | Sağ tık → **Yönetici olarak çalıştır**. Build/import süresini kısaltır |

Sunucu tarafını hiç derlemeyeceksen 4. adımı atlayabilirsin — ama o zaman **hiç maç kuramazsın**:
maç verisini yalnız sunucu üretir ve maçı yalnız bir admin başlatır.

**6. adımı atlama.** Windows Defender'ın gerçek zamanlı koruması her dosya açılışında araya girer;
IL2CPP build'i on binlerce `.cpp`/`.obj` üretip `Library/`'yi sürekli okuduğu için bu, paralel
derlemenin önünde kuyruk oluşturur — build ve import sürelerinde %20-40 bandında fark eder.
Betik repo kökünü, Unity kurulumunu, Unity/Hub cache'lerini, paket cache'lerini (`.gradle`,
`.nuget`, npm) ve build zincirinin exe'lerini dışlar; yolları kendi konumundan türetir, elle
düzenleme istemez. Geri alma: aynı betik `-Remove` ile.

> ⚠️ Dışlanan klasörler **artık taranmıyor** — oraya indirme yapma. Asset store paketini ya da
> GitHub'dan çektiğin arşivi önce başka bir yere indirip kontrol et.
> Ayrıntı, `-List`/`-Remove` kullanımı ve Dev Drive alternatifi: `scripts/README.md`.

> ⚠️ **Smart App Control açıksa dışlamalar işe yaramaz.** SAC bir antivirüs ayarı değil, Code
> Integrity politikasıdır: dışlama listesini okumaz. Unity **Burst**'ün `Library/BurstCache/JIT/`
> altına ürettiği **imzasız** DLL'i engeller (uyarıyı veren budur; `CodeIntegrity` olayı 3077) ve
> imzasız `deploy\*.exe` çıktılarımızı da engelleyebilir. Betik açılışta uyarır; kapatması
> *Windows Güvenliği → Uygulama ve tarayıcı denetimi → Akıllı Uygulama Denetimi*, ⚠️ **geri
> açılamaz**.

---

## 2. Rolünü ve sunucunu seç

`Tools > VortexArena > Development > Dev` penceresini aç.

- **Rol:** `player` (VR oyuncusu), `admin` (masaüstü gözlemci) ya da `weapon` (silah kavraması
  yakalama — `Docs/Gelistirici/Yemek-Kitabi.md`). Kısayol: **Ctrl+Alt+R**, üçünü sırayla döndürür.
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
