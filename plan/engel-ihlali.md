# Engel ihlali — kalan işler

Kod, protokol ve doküman **yazıldı**. Sistemin anlatımı dokümanlarda:
`Docs/ArenaNet-Protokol.md` §10.9 (kural + otorite + iki aşamalı ceza) · `Docs/Sistem-Ozeti.md` §4
(`ObstacleViolationProbe` · `ObstacleWarningOverlay` · `DamageVignette` · `ObstacleVolumes` ·
`ScreenFade` · `HmdOverlayBuilder`) ve §7 tuzaklar · `Docs/Gelistirici/Yapma-Listesi.md`
(layer sözleşmesi + `IsMuzzleBlocked` kapısı).

⚠️ **Protokol v11** — tel formatı **değişmedi**, ama istemci davranışı değişti: karışık sürümde eski
APK engelin içinden ateş edebilir ve kafası içerideyken görmeye devam eder. Yeni APK gerekir.

## 1. Sahne işi (elle) — her arena kutusunda tek tek

- [ ] **İç engellerin** (sütun, kasa, sandık, blok) collider'ını `Obstacle` layer'ına al.
      ⚠️ Dış duvar, zemin, tavan ve köşe dolgusu **girmez** — kalibrasyonu kaymış oyuncu durduk
      yere ölmesin.
- [ ] Yalnız **oyun kutusunun içindeki** geometri sayılır. Hazır environment paketleri kutunun
      çok dışına taşar (çit dizileri, terrain, hangar); oyuncunun ulaşamadığı hiçbir şey layer'a
      girmez — girerse yalnız `ObstacleVolumes`'un aday sınırını boşa doldurur.
- [ ] Sınır kabuğu olarak konmuş bloklar obje obje ayrılır: oyuncuyu dışarı çıkmaktan alıkoyan
      kabuk layer'a **girmez**, kutunun içinde duran blok girer. Aynı ad ailesi ikisini de
      kapsayabildiği için ada bakarak karar verilmez.
- [ ] Aynı objelerin collider'ı **konveks** olmalı (`MeshCollider` + `Convex`, ya da
      Box/Sphere/Capsule). ProBuilder objelerinde bu kutu varsayılan olarak KAPALI gelir.
      ⚠️ Environment paketleri konkav `MeshCollider` ile gelir: böylesine layer vermek hiçbir şey
      yapmaz (çalışma anında elenir + hata basar), mesh'i convex işaretlemek ise hull'ü şişirip
      oyuncuyu boşlukta cezalandırır. Doğru hamle kaba bir **Box/Capsule eklemektir** — yani bu
      iş çoğu arenada "layer değiştir" değil, "collider koy".
- [ ] `Tools > VortexArena > Arena > Engel Hacimlerini Denetle` koştur; rapordaki konveks olmayan
      **ve şişkin** collider'lar düzeltilene kadar o objeler yanlış ceza üretir.

Lobi sahnelerinde bu iş gerekmez: hasar kapısı fazdır (`playing`), lobide maç yoktur.

⚠️ Hangi arenada yapıldığı sahne açılmadan görülemez (layer değeri yalnız sahne objesinde durur):
her `Venues/<İşletme>/Scenes/<Arena>/` kutusunu açıp denetimi koştur.

## 2. VFX (asset yok)

- [ ] Engel yüzeyindeki kesişim efekti — **havuzlanmış tek** partikül sistemi, ihlal başına
      `Instantiate` YOK. Karartma, uyarı yazısı, vinyet ve titreşim koddan geliyor; eksik olan
      yalnız partikül.

## 3. Doğrulama (kullanıcı koşar)

- [ ] Yeni APK (tüm başlıklara)
- [ ] Kafayı engele sok → ekran **0.2 sn'de tam siyah**, uyarı yazısı nabızla görünür
- [ ] Bloğa **yavaşça** yaklaş → ekran, bloğun yüzeyi kırpılmaya başlamadan ÖNCE siyah;
      duvarın içi/arkası hiçbir anda görünmüyor. **Dört ana yönde ve köşegende ayrı ayrı dene**
      (eski kusur yön-bağımlıydı: köşegende sızıntı en genişti)
- [ ] Aynısını hızlı bir kafa dönüşüyle (bloğa doğru savrularak) tekrarla → açık kalan tek kare yok
- [ ] Bloğun **yanından** yürüyerek geç (kafa cisme değmiyor) → ekran KARARMIYOR
      (yalancı pozitif kontrolü; açıklık tavanı kafa yarıçapıdır)
- [ ] Elde silah tutarken silahın gövdesi kırpılmıyor (near-clip 0.10 → 0.05 düştü) ve uzak
      geometride z-fighting yok
- [ ] Yarım sok (kabuk değiyor, merkez dışarıda) → hafif kızarma, yazı YOK, ceza YOK
- [ ] İçeride kal → **3 sn hiç can gitmez**, sonra kırmızı vinyet siyahın üstünde nabız atar ve
      5 sn'de ölüm (toplam ~8 sn)
- [ ] 3 sn dolmadan çık → can hiç azalmamış; tekrar gir → tolerans baştan
- [ ] Yaralıyken (ör. 40 HP) gir → 3 sn sonra ~2 sn'de ölüm (beklenen davranış)
- [ ] Elini engele sok, kafanı dışarıda tut → **ceza yok, karartma yok** ama **tetik ölü**
- [ ] Bloğun İÇİNE gir, silahı dışarıda tut, ateş et → **atış yok** (asıl hile senaryosu)
- [ ] Silahı tuğlanın arkasına iyice geçir, yalnız namlu ucu boşlukta → **atış yok** (gövde kutusu)
- [ ] Namluyu engelin içine sok, tetiğe bas → **atış yok**, cephane sabit, kuru tık + kısa titreşim
- [ ] Namlunun ucunu ince bir siperin öbür yüzüne geçir → yine atış yok
- [ ] Siperin üstünden/yanından meşru atış → engellenmiyor (yalancı pozitif kontrolü — silah kutusu
      siperin birkaç cm yanındayken tetik ölmemeli)
- [ ] Bir kumandayı kapat, engelin yanında ateş et → **engellenmiyor** (izlenmeyen el sorulmaz)
- [ ] Mermiyle hasar al (engel dışında) → kırmızı vinyet aynı şekilde görünüyor
- [ ] Engelde ölünce: kill feed "… engelde kaldı", skor **değişmez**, `deaths` artar
- [ ] Engelin **içinde** kalarak bekle → canlanma **olmaz** ("Engelden çık ve canlan"); çık →
      canlanır. Bayrak takılı kalırsa 40 sn'de yine canlanır (tavan)
- [ ] Ölüp canlan (engelden çıkıp) → tolerans sıfırdan başlıyor, canlanır canlanmaz can gitmiyor
- [ ] Ölü oyuncunun soketini kopar/geri getir → istemci **canlı görünmez**, ölüm ekranı kapanmaz
- [ ] Kalibresiz oyuncu ihlalde **ceza almaz**
- [ ] Sınır karartması ile ihlal karartması aynı anda → titreme YOK (en yüksek alfa çizilir)
- [ ] Admin: ihlal eden oyuncunun halkası kırmızı yanıp söner; admin gözlemcide hiçbir ekran
      katmanı çizilmez (rig kökü kapalı)
- [ ] Karartma alfa 1'de gerçekten opak ve FOV'un tamamını kapatıyor mu (çevresel görüşte kalan bir
      şerit hilenin tamamını geri verir); uyarı yazısı ve vinyet siyahın **üstünde** okunuyor mu
