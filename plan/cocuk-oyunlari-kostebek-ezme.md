# Çocuk Oyunları — Köstebek Ezme: kalan iş

Kod, protokol ve doküman yerinde (protokol sürümü **değişmedi**). Sistemin anlatımı dokümanlarda:
mod sözleşmesinin tamamı (tür, olay, `stage`/`s`, nonce kapısı, skor kanalları, `modeState`)
`Docs/ArenaNet-Protokol.md` §10.5 · sunucu ve istemci bileşenlerinin sorumlulukları
`Docs/Sistem-Ozeti.md` §4 (`Modes/MoleMode` + `VortexArena.Modes.Mole` kutusu) · kural şekli
`Server/README.md` mod tablosu · yarışmalı çocuk oyununun reçetesi
`Docs/Gelistirici/Yemek-Kitabi.md` "Çocuk oyunu eklemek".

## Kalan içerik işi

- [ ] **Kalıcı ses ve efektler:** doğru/yanlış vuruş sesi `Modes/Mole/Audio/SFX_Mole_*_TEMP.wav`
      (sentetik, geçici), partiküller `NO_mole_hole` altındaki `FX_Correct`/`FX_Wrong` — moda özel
      materyali yok, namlu alevi ve toz materyali ödünç. Çıkış/iniş sesleri yerinde. Klipler
      `MoleHole` alanlarına sürüklenir; `_TEMP` dosyaları değiştirilince silinir. ⚠️ Ayrı bir "ezilme" sesi yoktur: doğru/yanlış vuruş sesi onun yerine geçer.
- [ ] Köstebek haritalarının `MapDefinition`'ında `ambienceClip` boş (müzik dolu) — arenaya uygun
      bir ortam sesi seçilecek.
- [ ] Açık hava köstebek sahnesinde çit hattı arena düzlemiyle hizalı değil (batı ve kuzey çitleri
      oyun alanının içinde kalıyor); çitler `VA_ArenaBoundary` düzlemine oturtulacak. Delikler
      düzlem **ve** çit kesişiminin içinde dizildi, çit taşınınca yerinde kalır.

## Playtest ayarları

- [ ] `MinSwingSpeed` (dokunarak ezmeyi kapatan eşik) ve balyoz ucundaki vuruş küresinin yarıçapı —
      küçük küre "ıskaladım" hissi, büyük küre "değmeden ezdim" hissi verir.
- [ ] Çıkış aralığı / ayakta kalma süresi / aynı anda ayakta köstebek tavanı (`MoleMode` sabitleri)
      ve köstebek klip hızları (`AC_Mole` durum `speed`'leri) — kalabalıkta yoğunluk ve çocuk için
      vurma rahatlığı.
- [ ] Puan ve ceza oranı; ceza caydırmıyorsa artırılır.
- [ ] Yanlış vuruş rengi (`wrongColor`): iki takım renginden de yeterince ayrılıyor mu — çocuk
      "yanlışa vurdum"u puandan değil oradan anlıyor.
- [ ] Köstebeğin yükseklik ayarı: eğilme derinliği çocuk için konforlu mu.
- [ ] Takım skorunun `0` tabanı: sahada bilinçli yanlış vurma görülürse eksiye açmak caydırıcılığı
      artırır (karar gerekçesi `Server/README.md` mod bloğunda).
- [ ] Oyuncu çarpışması görülürse tavan düşürülür — sunucudan mesafe çözümü yoktur.

Doğrulama listesi Notion'da: Todo → "Doğrulama 35 — Çocuk Oyunları: Köstebek Ezme (davranış testi)";
içerik işi "Çocuk oyunları + kavrama: kalan içerik işi" kartında.
