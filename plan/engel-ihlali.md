# Engel ihlali — kalan işler

Kod, protokol ve doküman **yazıldı**. Sistemin anlatımı dokümanlarda:
`Docs/ArenaNet-Protokol.md` §10.9 (kural + otorite + iki aşamalı ceza) · `Docs/Sistem-Ozeti.md` §4
(`ObstacleViolationProbe` · `ObstacleWarningOverlay` · `DamageVignette` · `ObstacleVolumes` ·
`ScreenFade` · `HmdOverlayBuilder`) ve §7 tuzaklar · `Docs/Gelistirici/Yapma-Listesi.md`
(layer sözleşmesi + `IsMuzzleBlocked` kapısı). Arena başına sahne kuralı (hangi collider
`Obstacle`'a girer, konvekslik, denetim aracı) `Docs/Gelistirici/Yemek-Kitabi.md` yeni arena
reçetesindedir — yeni arena o adımı atlarsa ihlal orada hiç çalışmaz.

⚠️ **Protokol v11** — tel formatı **değişmedi**, ama istemci davranışı değişti: karışık sürümde eski
APK engelin içinden ateş edebilir ve kafası içerideyken görmeye devam eder. Yeni APK gerekir.

## 1. VFX (asset yok)

- [ ] Engel yüzeyindeki kesişim efekti — **havuzlanmış tek** partikül sistemi, ihlal başına
      `Instantiate` YOK. Karartma, uyarı yazısı, vinyet ve titreşim koddan geliyor; eksik olan
      yalnız partikül.

## 2. Kırmızı çerçeve (`DamageVignette`) — yoğunluk ve öldüren vuruş

Bileşen `Assets/_Shared/Core/Player/DamageVignette.cs`; örneği `VA_CameraRig.prefab` →
`CenterEyeAnchor/DamageVignette`; yarıçaplar `Assets/_Shared/Materials/M_DamageVignette.mat`.
⚠️ Ayar alanları **prefabda serileştirilmiştir**: yalnız koddaki varsayılanı değiştirmek build'e
işlemez; prefab değeri + kod varsayılanı **birlikte** değişir (ayrışırsa Inspector kodu yalanlar).
Materyalin `_BaseColor`'ı çalışma anında property block ile ezilir — renk koddan gelir, materyalden
değil.

- [ ] **Daha görünür çerçeve.** Üç kol, etkisi büyükten küçüğe: (1) materyal yarıçapları
      `_InnerRadius` 0.18 → 0.14 · `_OuterRadius` 0.36 → 0.32 — değer ≈ tan(açı) × 0.46, kırmızı
      FOV'un içine doğru ~4° ilerler; "ekranın ne kadarı kırmızı" kolu budur, alfa değil;
      (2) `maxAlpha` 0.55 → 0.75; (3) `minHitIntensity` 0.35 → 0.50 (hafif vuruş da belirgin).
      Renk değişmez: `.linear` dönüşümü bilinçlidir (kod yorumu), doygunluğu düşük kırmızı konfor
      kararıdır. Nihai sayı sahada verilir; buradakiler başlangıç değeridir.
- [ ] **Öldüren vuruşun çerçevesi.** `LateUpdate`'teki ölü dalı zarfı sıfırlayıp 0 çiziyor; ölüm
      `SetHp(0)` ile aynı geri çağrıda işlendiği için son vuruş hesaplanıp aynı karede atılıyor.
      Yön göstergesi ve titreşim öldüren vuruşta çalışırken çerçevenin susması tutarsızdır.
      Kural: ölüyken **yalnız vuruş zarfı** çizilir (`HitEnvelope() * maxAlpha`, ~0.5 sn'de
      kendiliğinden söner); düşük can nabzı (`LowHpAlpha`, hp = 0'da sonsuza dek nefes alırdı) ve
      engel erimesi katmanı ölüyken kapalı kalır. Doküman: `Docs/Sistem-Ozeti.md` `DamageVignette`
      maddesindeki "ölüyken çizilmez" cümlesi.

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
- [ ] Yarım sok (kabuk değiyor, merkez dışarıda) → ekran **kararır** (kabuk değer değmez tam
      siyah; yarı saydam perde duvarın arkasını okutur), uyarı yazısı YOK, ceza YOK
- [ ] İçeride kal → **3 sn hiç can gitmez**, sonra kırmızı çerçeve siyahın üstünde **1 Hz düz
      nabızla, belirgin** yanar ve 5 sn'de ölüm (toplam ~8 sn); nabız seğirmiyor, titreme yok
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
- [ ] Engelin **içinde** kalarak bekle → canlanma **olmaz** ("Engelden çık ve canlan"), ne kadar
      beklersen bekle; çık → canlanır (40 sn tavanı sunucu içidir, oyuncuya görünmez)
- [ ] Ölüp canlan (engelden çıkıp) → tolerans sıfırdan başlıyor, canlanır canlanmaz can gitmiyor
- [ ] Ölü oyuncunun soketini kopar/geri getir → istemci **canlı görünmez**, ölüm ekranı kapanmaz
- [ ] Kalibresiz oyuncu ihlalde **ceza almaz**
- [ ] Sınır karartması ile ihlal karartması aynı anda (vücut alan dışı, kafa engelde) → titreme
      YOK, tek uyarı yazısı ("duvar" kazanır, "alan dışı" gizli), can gidiyor (engel kuralı)
- [ ] Admin: ihlal eden oyuncunun halkası kırmızı yanıp söner; admin gözlemcide hiçbir ekran
      katmanı çizilmez (rig kökü kapalı)
- [ ] Karartma alfa 1'de gerçekten opak ve FOV'un tamamını kapatıyor mu (çevresel görüşte kalan bir
      şerit hilenin tamamını geri verir); uyarı yazısı ve vinyet siyahın **üstünde** okunuyor mu
- [ ] Engel yüzeyinde bir dekor-harita arenasında (görünmez duvar bloğu) ve bir mobilya arenasında
      (masa/dolap) aynı davranış: kafa → karartma, namlu → atış yok
