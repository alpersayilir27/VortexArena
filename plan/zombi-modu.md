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
| Hedef | Oyuncular **tek ekip**; zafer ve yenilgi ekibindir, kişisel puan yalnız sonuç ekranında öne çıkar |
| Ölüm | Düşen oyuncuyu **takım arkadaşı kaldırır**; kaldırılmayan dalga arasında kalkar; herkes aynı anda düşerse yenilgi |
| Maç sonu | Süre dolunca değil **Son Direniş**le biter: son 90 sn'de Dev gelir; Dev ölürse ya da süre biterse zafer |
| Zombi türü | Yürüyen · Sürünen · Koşucu · İri · Çığlıkçı + Son Direniş'in Dev'i; tür sunucuda seçilir, gözü renginden okunur |
| Hız tavanı | 1,2 m/sn güvenlik kuralı tür için de geçerli — "hızlı" zombi tavanda koşar, tavanı aşmaz |
| Kovalama | Kuş uçuşu en yakın değil **yol mesafesi + görüş + kalabalık dengesi**; hedef 2 sn kilitli; saldırı geri adımla boşa çıkar |

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
- [ ] Kademe tablosunun yenisi Faz 11'dedir.

## Faz 8 — Ekip kuralları ve maç sonu

Kod, doküman ve prefab'lar bitti (arkadaşın kaldırması, dalga arası diriliş, ekip yenilgisi,
süre dolunca zafer, türe göre puan, moda özel sonuç ekranı, takım şeridi + düşen arkadaş işareti).
Kalan:

- [ ] Gözlükte doğrulama: kaldırma mesafesi/süresi doğal mı, düşen arkadaş işareti duvar arkasından
      okunuyor mu, sonuç ekranı 8'den fazla oyuncuda taşıyor mu (tablo 8 satır).
- [ ] Takım şeridi ve zombi sonuç ekranı için `Docs/Gelistirici/Arayuz/zombi.html` maketi
      (ölçüler builder'da tahminle verildi).

## Faz 9 — Zombi türleri, dalga dizilimi, Son Direniş

Tür sunucuda seçilir ve zombinin payload'una `t:` anahtarıyla yazılır (bilinmeyen anahtar
atlandığı için protokol sürümü bunun için artmaz). Hız ve davranış beyinde, görünüş istemcide,
can ve hasar sunucuda aynı tablodan okunur. Can tür başına farklı olduğu için sunucuya **nesne
başına en yüksek can** ataması eklenir (bugün can nesne türünden sabit 100).

| Tür | Can | Hız | Hasar | Davranış | Görünüş (göz) |
|---|---|---|---|---|---|
| Yürüyen | 100 | dalga hızı (0,7 → 1,2) | dalga hasarı | Temel | Ghoul, Cop (sarı) |
| Sürünen | 60 | ×0,45 | ×0,6 | Alçak: kafası diz hizasında, aşağı nişan ister | Ghoul, Cop sürünerek (sarı) |
| Koşucu | 60 | daima 1,2 | ×0,75 | Koşu animasyonu, uzaktan doğar | Girl (kırmızı) |
| İri | 400 | 0,6 | ×1,6, tavan 60 | ×1,3 boy, vuruş menzili 1,5 m, vurulunca sendelemez | Soldier ×1,3 (turuncu) |
| Çığlıkçı | 80 | dalga hızı ×0,9 | yakında ×0,5 | Hedefine 6–8 m'de durur; 10 sn'de bir çığlık: 8 m içindeki zombiler 6 sn öfkelenir (hız ×1,25, tavan 1,2) ve onun hedefine döner | Clown (mor) |
| Dev | 2500 × oyuncu (en az 2) | 0,7 | 45, bekleme 2 sn | Yalnız Son Direniş; ×1,6 boy; 15 sn'de bir çığlıkla yanına 3 yürüyen çağırır | Soldier ×1,6 (beyaz) |

Zorluk: Kolay can/hasar ×0,7 ve her tür bir dalga geç; Zor ×1,3 ve bir dalga erken. Ayakta tavanı
30 kalır; iri en çok 3, çığlıkçı en çok 2 aynı anda.

Normal dizilim (dalga toplamının payı; iri ve çığlıkçı adet):

| Dalga | Yürüyen | Sürünen | Koşucu | İri | Çığlıkçı | Not |
|---|---|---|---|---|---|---|
| 1 | %100 | — | — | — | — | Isınma |
| 2 | %85 | %15 | — | — | — | |
| 3 | %70 | %10 | %20 | — | — | |
| 4 | %65 | %10 | %25 | 1 | — | İlk iri |
| 5 | %30 | — | %70 | — | — | **Koşucu sürüsü**, toplam ×0,8 |
| 6 | %55 | %10 | %25 | 2 | 1 | İlk çığlıkçı |
| 7 | %50 | %10 | %30 | 2 | 1 | |
| 8 | geri kalan | — | — | 4 | — | **Ağır adımlar**, toplam ×0,7 |
| 9+ | %45 | %10 | %35 | 2 + ⌊(d−9)/2⌋ | 2 | 11, 14, 17… özel dalga sırayla döner (sürü · ağır · **Çığlık gecesi**: 3 çığlıkçı) |

- [ ] **Son Direniş** — kalan süre 90 sn'ye inince (300 sn'den kısa maçta 60 sn) o anki dalga
      doğurmayı keser; "SON DİRENİŞ" bandı, bütün silahlar açılır, düşmüş herkes kalkar, Dev
      oyuncunun en kalabalık olduğu katta doğar, normal doğum ayakta tavanında sürer, müzik en
      yüksek katmana çıkar. Dev ölünce sahadaki zombiler çöker → "KURTULDUNUZ".
- [ ] **HUD** — Dev can barı, Son Direniş geri sayımı, çığlıkçı çığlığı uzaktan duyulur.
- [ ] Sürünen için Mixamo'da sürünen saldırı klibi aranır; yoksa saldırı anında sürünme klibi
      hızlanır + ısırık sesi.

## Faz 10 — Kovalama ve yapay zekâ (beyin, yalnız admin build)

- [ ] **Hedef puanı** — NavMesh yol uzunluğu (zombi başına 0,5–1 sn aralıkla, kareye dağıtılmış
      `CalculatePath`); görüş hattı yoksa ×1,5; o oyuncuyu kovalayan zombi adil payı (ayakta ÷
      yaşayan oyuncu) aşıyorsa fazla başına +2 m; son 3 sn'de 15 m içinde ateş eden oyuncu ×0,8
      (silah sesi çeker — biri çekerken diğerleri yandan vurur).
- [ ] **Histerezis** — hedef en az 2 sn kilitli; yeni aday ancak %30 daha iyiyse; saldırı
      menzilindeyken değişmez.
- [ ] **Görüş hattı** — zombi göz hizasından oyuncu kafasına tek ışın, yalnız çevre katmanları,
      zombi başına 0,5 sn'de bir, kare başına ışın bütçesiyle.
- [ ] **Kaçılabilir saldırı** — 0,4 sn hazırlığın sonunda yatay mesafe 1,6 m'yi aşıyorsa vuruş
      boşa gider (savurur, ıskalar). Free-roam'da geri adım atmak işe yarar.
- [ ] **Hedefsiz zombi tek katta da durmaz** — çok katlıdaki gezinme tek katta da açılır, en
      yakın oyuncu bölgesine doğru.
- [ ] **Takılı zombi bekçisi** — yolu olduğu hâlde 8 sn'de 0,5 m ilerlemeyen zombi doğum
      noktasına sisle ışınlanır; dalganın kilitlenmesini önler.
- [ ] **Oyuncu bedeni** — beyin makinesinde her yaşayan oyuncunun kafa izdüşümünde oyulmayan
      (carve kapalı) `NavMeshObstacle` kapsülü: zombi yolundaki oyuncunun içinden geçmez.
- [ ] **Build Readiness satırı** — Zombi türündeki arenada NavMesh verisi, her katta en az bir
      `zombie_spawn` ve silah tuvali var mı.

## Faz 11 — Silah ilerlemesi

Kademe her yeni dalgaya bir **rol** ekler; en güçlü silah son kademededir (bugünkü tabloda en zayıf
silah 5. dalgada, M4A4 M4A1'den sonra açılıyor).

| Dalga | Açılan | Rol |
|---|---|---|
| 1 | mp9, ump45 | Kolay tutuş, yakın savunma |
| 2 | nova, p90 | Pompalı (sürünen/koşucu), geniş şarjör |
| 3 | famas, scarl, **bomba** | İlk tüfekler; bomba kalabalığa |
| 4 | m4a1, g36c, m4a4 | İsabet — çığlıkçıyı uzaktan düşürmek |
| 5 | xm1014, ak47 | İri avcısı: otomatik pompalı, kafada tek atış |
| 6 | galilar, aug | Üst kademe |
| Son Direniş | hepsi | |

- [ ] `ZombieArsenal` tablosu yukarıdaki gibi; bomba bilek kılıfı zombi modunda kademeye bağlanır
      (bugün 1. dalgadan açık).
- [ ] Son Direniş'te bütün kilitler açılır (HUD "BÜTÜN SİLAHLAR AÇIK").

## Protokol etkisi (kod yazılmadan önce `ArenaNet-Protokol.md`'ye)

- Zombi payload'unda `t:` (tür), mod durum dizesinde Son Direniş, Dev canı ve `over:rescued`
  (değer protokolde ayrıldı, üretilmiyor).
- Nesne başına en yüksek can ataması sunucu içidir; istemciye canın kendisi bugünkü nesne
  durumuyla akar.

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
| Zafer (Kurtuldunuz / Dayandınız) | 1 + 1 | 4–6 sn |
| Son Direniş bandı | 1 | 2–3 sn |
| Çığlıkçı çığlığı (uzaktan seçilir) | 2 | 1,5–2,5 sn |
| Dev: kükreme · adım · darbe · ölüm | 2 + 3 + 2 + 1 | 0,5–4 sn |
| Koşucu hırıltısı (yürüyenden tiz) | 3 | 1–2 sn |
| Kaldırma (ilerleme + kalktı) | 1 + 1 | 0,5–3 sn |
| Yeni silah açıldı | 1 | 1–2 sn |
| Geri sayım tıkı | 1 | 0,5 sn |
| Ambiyans: mezarlık · konak | 1 + 1 | 2–3 dk |
| Türkçe anons (isteğe bağlı) | 6–10 | ~1 sn |
