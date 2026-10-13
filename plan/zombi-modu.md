# Zombi modu (`zombie`) — kalan iş

Kooperatif dalga savunması. Protokol sözleşmesi `Docs/ArenaNet-Protokol.md` §10.5 (`zombie` bloğu +
denge tablosu) ve §10.10 ("Sunucu-atanmış sahiplik"); bileşenler `Docs/Sistem-Ozeti.md` §4;
reçete `Docs/Gelistirici/Yemek-Kitabi.md` §11.7. Sanat projeleri `blender/ZombieManor/` (konak) ve
`blender/Graveyard/` (mezarlık).

Fazlar sırayla yapılır; her faz derleme + aynı commit'te doküman + Notion test kartıyla kapanır.
Görsel seçim gereken adımda (HUD maketi, ışık varyantı, zombi adayı) kullanıcıya sorulur.

## Verilmiş kararlar

| Konu | Karar |
|---|---|
| Zorluk | Admin maçtan önce seçer: **Kolay / Normal / Zor** |
| Boş kat | Oyuncu olmayan katta da zombi durur; en az sayı zorlukla: Kolay 2 · Normal 3 · Zor 4 |
| Silah | Zombiye özel tuval, silahlar **dalgayla açılır** (kilitli kart + "DALGA N") |
| Doğuş | Sis bulutu + silahtaki dissolve; yer değiştiren zombi de aynı efektle çıkar |
| Zombi | Gerçekçi, Mixamo karakter + animasyon (aday görselleriyle seçim) |
| Kan | Koyu kırmızı; kafadan vuruşta 2× |
| Müzik | Dinamik iki katman: dalga arası gerilim · dalga sırası aksiyon, crossfade + vurgu sesleri |
| Atmosfer | Ay ışığı, mavi-gri sis, sıcak fener adacıkları (varyant ekran görüntüsüyle son seçim) |

## Faz 1 — Oynanış temeli

Kod, sahne verisi ve doküman bitti (duvar collider'ı + NavMesh, doğma noktaları, kat kotası,
zorluk). Kalan:

- [ ] Sunucu derlemesi + sunucu/admin/APK dağıtımı ve saha doğrulaması.

## Faz 2 — Zombi görünüşü, animasyon, doğuş

Beş gövde, denetleyici, kemiklerdeki vuruş kutuları, doğuş sisi ve dissolve bitti
(`ZombieLookBuilder`). Kalan:

- [ ] Gözlükte doğrulama: göz küreleri doğru yerde mi (`ZombieLook`'taki göz sapmaları gövde başına
      elle ayarlanır), vuruş kutuları yürürken/sürünürken gövdeye oturuyor mu, ayaklar kayıyor mu
      (`LocoRate`), doğuş animasyonu bitmeden zombi yürümeye başlıyor mu (`spawnHoldSeconds`).
- [ ] Gözlükte kare bütçesi: dalga doluyken deri hesabı (30 × 8k üçgen, gölge kapalı).
- [ ] İlk zombi görününce takılma var mı: deri + dissolve materyallerinin varyantları o karede
      derlenirse sahne yüklemesindeki ısıtmaya eklenir (`performans.md` shader ısınması).

## Faz 3 — Vuruş hissi

Kan efekti bitti (gövde + kafada iki kat, herkes görür). Kalan:

- [ ] Gözlükte doğrulama: kan karanlıkta seçiliyor mu, kafa vuruşu ayırt ediliyor mu.
- [ ] Zombi oyuncuya vurunca darbe sesi + görüş kenarında kısa kırmızı flaş.

## Faz 4 — Ses ve müzik

- [ ] Yer tutucu klipleri **kalıcı seslerle değiştir** (`ZombieSounds` asset'i; yer tutucular ve
      kaynakları `Assets/Modes/Zombie/Audio/Placeholder/`); istenen liste aşağıda.
- [ ] Gözlükte alan doğrulaması: inleme aralığı ve aynı anda inleyen tavanı, 3B mesafe, klip
      seviyeleri/perde sapması, müzik seviyesi ve geçiş süresi — hepsi `ZombieSounds` üstünden.
- [ ] Zombi `ModeAudioRegistry` kuralları (`ModeAudioGameType`'a `Zombie` sona eklenir).

## Faz 5 — Atmosfer

Seçilen ayar iki arenada uygulandı ve bake edildi (Linear sis 2,5–14 m, ortam 0,5, alçak zayıf ay,
`M_Sky_MoonlitNight`, kısık iç lambalar, konakta avlu + teras yer sisi). Kalan:

- [ ] Gözlükte karanlık/okunabilirlik kontrolü (zombiler parlayan göz + soluk öz-ışıkla seçilir;
      silah panoları lambalı).
- [ ] Konakta ölçek hissi (kat 3,5 m, tavan 3,3 m, kapı 2,4 m): hâlâ "minyatür" geliyorsa testte
      konsoldaki `[Kalibre] hizalama sonrası kafa yüksekliği` satırı oyuncunun gerçek göz
      yüksekliğiyle karşılaştırılır.
- [ ] İsteğe bağlı: kapı/pencere ağzına sis kartı, lamba titremesi (yalnız emissive/hale).

## Faz 7 — Silah ilerlemesi

Kod, kilit görünümü, açılış tablosu ve doküman bitti. Kalan:

- [ ] Gözlükte doğrulama: kilit plakası okunuyor mu, kilitli silah gerçekten alınamıyor mu, açılış
      nabzı + HUD bildirimi dalga başında geliyor mu.
- [ ] Kademe tablosu (`ZombieArsenal` asset'i) saha dengesine göre son hâline getirilir (bomba
      kademesi atılabilir eşya geldiğinde eklenir).

## Kalıcı ses/müzik listesi (geçicilerin yerine)

Efekt mono WAV 48 kHz; müzik/ambiyans stereo, dikişsiz döngü.

| Ses | Adet | Uzunluk |
|---|---|---|
| İnleme/hırıltı | 8–10 | 1,5–4 sn |
| Doğuş çığlığı | 3 | 1–2 sn |
| Doğuş sisi | 2 | 1–1,5 sn |
| Saldırı (hırlama + savurma) | 4 | 0,6–1,2 sn |
| Oyuncuya darbe | 3 | 0,3–0,6 sn |
| Zombiye kurşun (et) | 5 | 0,2–0,4 sn |
| Kafadan vuruş | 2 | 0,3–0,5 sn |
| Ölüm | 4 | 1–2 sn |
| Ayak sürüme (isteğe bağlı) | 4–6 | 0,4–0,8 sn |
| Müzik: dalga arası gerilim | 1 | 60–90 sn |
| Müzik: dalga aksiyonu (aynı tempo/ton) | 1 | 90–120 sn |
| Dalga başladı / temizlendi | 1 + 1 | 2–4 sn |
| Maç kaybı | 1 | 4–6 sn |
| Yeni silah açıldı | 1 | 1–2 sn |
| Geri sayım tıkı | 1 | 0,5 sn |
| Ambiyans: mezarlık · konak | 1 + 1 | 2–3 dk |
| Türkçe anons (isteğe bağlı) | 6–10 | ~1 sn |

## Karar bekleyenler

- [ ] **Aradaki oyuncu:** başka hedefe yürüyen zombi yolundaki oyuncunun bedeninden geçer. Aday:
      uzak oyuncuda `NavMeshObstacle` (yalnız sahip makinede anlamlı) — maliyeti ölçülmeli.
- [ ] **Build Readiness satırı:** `zombie` destekleyen arenada NavMesh verisi var mı kontrolü.
- [ ] **Zombi çeşitleri:** dalgaya göre hızlı/iri tür — model + tür anahtarı + denge satırı ister.
- [ ] **Görüş hattı:** en yakın oyuncu duvar arkasından da kovalanır.
- [ ] **Boşluk bölgesi** (konak üst katları): `zombie-manor-sanat.md`.
