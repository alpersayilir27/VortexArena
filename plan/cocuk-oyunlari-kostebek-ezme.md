# Çocuk Oyunları — Köstebek Ezme: kalan iş

Kod, protokol ve doküman yerinde (protokol sürümü **değişmedi**). Sistemin anlatımı dokümanlarda:
mod sözleşmesinin tamamı (tür, olay, `stage`/`s`, nonce kapısı, skor kanalları, `modeState`)
`Docs/ArenaNet-Protokol.md` §10.5 · sunucu ve istemci bileşenlerinin sorumlulukları
`Docs/Sistem-Ozeti.md` §4 (`Modes/MoleMode` + `VortexArena.Modes.Mole` kutusu) · kural şekli
`Server/README.md` mod tablosu · yarışmalı çocuk oyununun reçetesi
`Docs/Gelistirici/Yemek-Kitabi.md` "Çocuk oyunu eklemek".

## Kalan içerik işi

⚠️ **Sahnedeki her şey prototiptir:** delik bir halka (yassı silindir), köstebek bir kapsül, balyoz
silindir sap + kutu baş. Gerçek modeller gelince yerine geçecek; **yerleşim korunmalı** — köstebeğin
yükselme mesafesi ve deliğin çapı ona göre ayarlı.

- [ ] **Gerçek modeller + animasyonlar:** köstebek (çıkış / bekleme / ezilme / iniş), delik halkası,
      balyoz. ⚠️ Yükseliş animasyonu sunucunun havada kalma penceresinin **içinde** bitmeli — dışına
      taşarsa sunucunun indirdiği köstebek ekranda hâlâ tırmanıyor görünür.
      ⚠️ **Köstebek modeli için tek dokunulacak yer `Mole/Model`:** prototip parçaları (gövde, kafa,
      burun, göz, pençe) silinip gerçek model oraya konur. Ayakları pivotun orijinine oturmalı
      (köstebek yükselince zemin hizasında durur), kafası bugünkü kafanın hizasında olmalı. Sonra
      takım rengini alacak görseller `teamRenderers`'a sürüklenir ve köstebeğin **trigger**
      collider'ı kontrol edilir. Kodda ölçü yoktur — vuruşun cevabı collider'lardan çıkar.
- [ ] **Balyoz kavrama pozu** (`Kavrama Pozu Stüdyosu`, sağ + sol ana kabza) — yazılmadan balyoz ele
      gelir ama kumanda anchor'ında, yanlış açıyla durur.
- [ ] **Ses klipleri:** çıkış, doğru vuruş (neşeli), yanlış vuruş (uyarı), iniş. Kancalar
      `MoleHole`'da hazır ve aşama DEĞİŞİMİNE bağlı; klipler alanlara sürüklenir, delik kökündeki
      `AudioSource` çalar. ⚠️ Ayrı bir "ezilme" sesi yoktur: doğru/yanlış vuruş sesi onun yerine
      geçer.
- [ ] **HUD sanatı** (bugün Hamburgerci HUD'ının kopyası).
- [ ] Sahnenin ortam sesi boş; köstebek arenasına uygun bir `ambienceClip` seçilecek.
- [ ] Açık hava köstebek sahnesinde çit hattı arena düzlemiyle hizalı değil (batı ve kuzey çitleri
      oyun alanının içinde kalıyor); çitler `VA_ArenaBoundary` düzlemine oturtulacak. Delikler
      düzlem **ve** çit kesişiminin içinde dizildi, çit taşınınca yerinde kalır.

## Playtest ayarları

- [ ] `MinSwingSpeed` (dokunarak ezmeyi kapatan eşik) ve balyoz ucundaki vuruş küresinin yarıçapı —
      küçük küre "ıskaladım" hissi, büyük küre "değmeden ezdim" hissi verir.
- [ ] Çıkış aralığı / aynı anda ayakta köstebek tavanı — kalabalıkta yoğunluk hissi.
- [ ] Puan ve ceza oranı; ceza caydırmıyorsa artırılır.
- [ ] Yanlış vuruş rengi (`wrongColor`): iki takım renginden de yeterince ayrılıyor mu — çocuk
      "yanlışa vurdum"u puandan değil oradan anlıyor.
- [ ] Köstebeğin yükseklik ayarı: eğilme derinliği çocuk için konforlu mu.
- [ ] Takım skorunun `0` tabanı: sahada bilinçli yanlış vurma görülürse eksiye açmak caydırıcılığı
      artırır (karar gerekçesi `Server/README.md` mod bloğunda).
- [ ] Oyuncu çarpışması görülürse tavan düşürülür — sunucudan mesafe çözümü yoktur.

Doğrulama listesi Notion'da: Todo → "Doğrulama 19 — Çocuk Oyunları: Köstebek Ezme".
