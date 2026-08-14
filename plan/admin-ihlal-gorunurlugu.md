# Admin ihlal görünürlüğü — kalan işler

Kod, protokol ve doküman **yazıldı**. Sistemin anlatımı dokümanlarda:
`Docs/ArenaNet-Protokol.md` §5.3 (`violation`) · §6.3 (bit7) · §10.9 (ihlal görünürlüğü) ·
`Docs/Sistem-Ozeti.md` §4 (`AdminViolations` · `AdminRoster` · `AdminPlayerMarkers` ·
`AdminStatsPanel` · `ArenaBoundary.Active`) · `Docs/Kullanim-Kilavuzu.md` (operatör dili).

⚠️ **Protokol v14** — tel formatı ve paket boyu **değişmedi** (bit7 rezervdeydi), ama biti yazan
istemcidir: eski APK'lı oyuncu alan-dışını hiç göndermez ve adminde **sessizce görünmez**.
Yeni APK gerekir.

⚠️ **Alan dışında ateş kapısı açıldı** (`ArenaCombat.CanFire` → `IsOutsideArena`, fail-open):
alanın dışına çıkıp içeri ateş etmek artık mümkün değil.

## 1. Prefab bağları (Unity, elle)

`AdminHud.prefab` → **`violationFeedText`** bağlandı (`ViolationFeed`, kill feed'in üstünde,
`UiKit.Accent`). Kalanlar:

- [ ] **`AdminPreferencesPanel.prefab`** → GÖRÜNÜM bölümüne **"İhlal sesi"** satırı:
      `Çatı` satırının (`Label_`/`Prev_`/`Value_`/`Next_`) dördünü çoğalt, adları `*_Ihlal sesi`
      yap, hepsini **40 px aşağı** al (`Çatı` y = −884 → yeni satır y = −924; `Fill` yüksekliği
      1042, yer var), `Label_` metnini `İhlal sesi` yap ve bileşendeki
      `_violationSoundValue` / `_violationSoundPrev` / `_violationSoundNext` alanlarına bağla.
      ⚠️ Bağlanmazsa **ses yine çalışır** (varsayılan açık), yalnız panelden kapatılamaz.
- [ ] **`AdminStatsPanel.prefab`** → `_columns` dizisine 12. eleman (`İHLAL` başlığı + kolon TMP'si).
      ⚠️ Bağlanmazsa kolon sessizce çizilmez, tablo bozulmaz (mevcut PING deseni).
- [ ] **`_Shared/Data/Resources/GameSoundBank.asset`** → `adminViolation` klibi
      (`Assets/Audio/Announce/` altına kısa bir uyarı sesi). ⚠️ Atanmazsa uyarı sessizdir.

## 2. Doğrulama (kullanıcı koşar)

- [ ] `dotnet build` (Server) + Unity derlemesi + yeni APK
- [ ] Alanın dışına çık → halka **turuncu 1.5 Hz**, etiket `ALAN DIŞI`, feed'de başlangıç satırı;
      içeri gir → söner + bitiş satırı ve süre
- [ ] Kafayı engele sok → halka **kırmızı 3 Hz** (mevcut davranış bozulmamış), etiket `DUVAR`
- [ ] Aynı anda alan dışı + kafa engelde → halka **kırmızı** (öncelik doğru)
- [ ] Alanın dışından içeri ateş et → **atış çıkmıyor**, cephane sabit
- [ ] Boyut dosyası bağlı olmayan bir sahnede → halka **hiç yanmıyor** ve tetik **kilitlenmiyor**
      (plansız muhafaza: açık başarısızlık, herkesi alan-dışı saymıyor)
- [ ] Sınır çizgisinde salın (0.3 sn'lik çıkışlar) → feed **kirlenmiyor**, halka yine de yanıp
      sönüyor, istatistikteki sayaç **artmıyor**
- [ ] Oyuncu öldür → ihlal halkası çizilmiyor, feed'e bitiş satırı düşüyor
- [ ] Poz akışını kes (oyuncu APK'sını dondur) → bayrak `OBSTACLE_FLAG_STALE_MS` içinde düşüyor,
      halka sonsuza kadar turuncu kalmıyor
- [ ] İki admin bağlı → **ikisi de aynı feed satırlarını** görüyor
- [ ] Hiç admin bağlı değilken ihlal başlat, sonra admin bağla → yarım kalmış ihlal **açık
      kalmıyor**, süre şişmiyor
- [ ] Kuş bakışından POV'a geç → halkalar kayboluyor ama **satır kenarlığı** ihlali göstermeye
      devam ediyor; ihlal bitince kenarlık eski rengine **geri dönüyor**
- [ ] Kalibresiz oyuncu alan dışına çıksın → halka yanıyor ve feed'e giriyor, **can gitmiyor**
- [ ] Lobide/geri sayımda alan dışına çık → feed **yine yazıyor** (defter fazdan bağımsız)
- [ ] Maç sonu istatistik panelinde ihlal sayacı doğru; lobiye dönüşte sıfırlanıyor
- [ ] Ses: beş oyuncu aynı anda ihlalde → uyarı sesi **3 sn'de bir**, üst üste binmiyor

## 3. Yapılmayanlar — ayrı karar bekliyor

Bu ikisi planın "önlem önerileri" başlığındaydı ve **bu tura dahil edilmedi**:

- **`warn_player` admin komutu** — seçili oyuncunun başlığında birkaç saniye büyük uyarı yazısı +
  titreşim. Sahada operatörün oyuncuya ulaşmasının başka yolu yok (mikrofon yok, oyuncu gözlükte).
  Yeni admin komutu (§5.2) + sunucu dalı + oyuncu tarafında HMD katmanı gerektirir.
- **Tekrarlanan ihlalin vurgulanması** — aynı oyuncu N ihlal / M saniye içinde → feed satırı
  kalınlaşır + ses bir kez daha çalar. Kasıtlı hileyi kazadan ayıran tek sinyal tekrardır.
  Defter (`count`) zaten telde olduğu için istemci tarafında tek başına yapılabilir.

## 4. Bilinen davranış

`Announced` durumdayken **bağlantısı kopan** oyuncu için feed'e kapanış satırı gitmez (süreye
kopukluğun geçtiği boşluk yazılmasın diye). Operatör kapanışsız bir başlangıç satırı görebilir;
oyuncu zaten listeden düşmüştür.
