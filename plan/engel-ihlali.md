# Engel ihlali — kalan işler

Kod, protokol ve doküman **yazıldı**. Sistemin anlatımı dokümanlarda:
`Docs/ArenaNet-Protokol.md` §10.9 (kural + otorite + iki aşamalı ceza) · `Docs/Sistem-Ozeti.md` §4
(`ObstacleViolationProbe` · `ObstacleWarningOverlay` · `DamageVignette` · `ObstacleVolumes` ·
`ScreenFade` · `HmdOverlayBuilder`) ve §7 tuzaklar · `CLAUDE.md` (layer sözleşmesi + araç satırı +
`IsMuzzleBlocked` kapısı).

⚠️ **Protokol v11** — tel formatı **değişmedi**, ama istemci davranışı değişti: karışık sürümde eski
APK engelin içinden ateş edebilir ve kafası içerideyken görmeye devam eder. Yeni APK gerekir.

## 1. Tek seferlik editör adımı

- [ ] `Tools > VortexArena > Arena > HMD Katmanlarını Kur` — rig prefabına uyarı yazısını ve hasar
      vinyetini kurar, vinyet materyalini üretir. Çalıştırılmadıkça karartma çalışır ama yazı ve
      kırmızı vinyet hiç çizilmez.

## 2. Sahne işi (elle) — kalan arenalar: `Arena12x12`, `VortexAntep`

- [ ] **İç engellerin** (sütun, kasa, sandık, blok) collider'ını `Obstacle` layer'ına al.
      ⚠️ Dış duvar, zemin ve tavan **girmez** — kalibrasyonu kaymış oyuncu durduk yere ölmesin.
- [ ] Aynı objelerin collider'ı **konveks** olmalı (`MeshCollider` + `Convex`, ya da
      Box/Sphere/Capsule). ProBuilder objelerinde bu kutu varsayılan olarak KAPALI gelir.
- [ ] `Tools > VortexArena > Arena > Engel Hacimlerini Denetle` koştur; rapordaki konveks olmayan
      **ve şişkin** collider'lar düzeltilene kadar o objeler yanlış ceza üretir.

Lobi sahnelerinde bu iş gerekmez: hasar kapısı fazdır (`playing`), lobide maç yoktur.

## 3. VFX (asset yok)

- [ ] Engel yüzeyindeki kesişim efekti — **havuzlanmış tek** partikül sistemi, ihlal başına
      `Instantiate` YOK. Karartma, uyarı yazısı, vinyet ve titreşim koddan geliyor; eksik olan
      yalnız partikül.

## 4. Doğrulama (kullanıcı koşar)

- [ ] `dotnet build` (Server) + Unity derlemesi + `HMD Katmanlarını Kur` + yeni APK
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
- [ ] Silah tezgâhının önünden ateş et → mermi kavrama hacimlerine takılmaz (trigger elemesi)
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
