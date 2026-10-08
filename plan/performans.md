# Performans — plan

Quest'te kare süresini ve takılmayı önce ölçülebilir hâle getirmek, ayarı ve kodu ölçüme göre
değiştirmek. Bugünkü cihaz ayarları ve neden öyle oldukları: `Docs/Sistem-Ozeti.md` →
`QuestRenderSettings`; çözünürlük kuralı `Docs/Gelistirici/Yapma-Listesi.md` ("Quest çözünürlüğünü
`Mobile_RPAsset.renderScale` ile ayarlamaya çalışma").

## 0. Sıra kuralı

- **Önce ölçüm (§1), sonra ayar ve kod (§2, §3).** Veri olmadan yapılan ayar hata vermez; yanlışsa
  yalnız ısınan gözlükte maç ortasında kare düşürür ve kimse sebebini bilmez.

## 1. Cihaz performans telemetrisi

Bugün `status.fps` 5 sn'lik ortalamadır: tek bir 200 ms takılma da ısıl kısma da ortalamada kaybolur.

- [ ] Protokol (önce doküman, `Docs/ArenaNet-Protokol.md` `status` satırı): isteğe bağlı alanlar
  `frameMaxMs` (penceredeki en kötü kare), `slowFrames` (hedef kare süresinin 1,5 katını aşan kare
  sayısı), `batteryTemp` (`OVRManager.batteryTemperature`). İsteğe bağlı oldukları için
  `PROTOCOL_VERSION` artmaz; `fps` ile aynı gerekçeyle roster'a ve `changed`'e girmez.
- [ ] İstemci: `ArenaClient`'ın kare sayacının yanında pencere maksimumu ve yavaş kare sayısı.
- [ ] Sunucu: eşik aşımı **geçişte** (her `status`'ta değil) oyuncu adıyla zaman damgalı loga
  yazılır; admin arayüzü yok.
- ⚠️ GPU yükü `OVRManager`'dan okunamaz (`gpuUtilSupported` bu kurulumda sabit `false`): CPU/GPU
  darboğazı ayrımı cihazda OVR Metrics Tool ile yapılır.

## 2. Ölçüme bağlı Quest ayarları

- [ ] **90 Hz** (`OVRManager.display.displayFrequency`; varsayılan 72 Hz): §1'in verisi en ağır
  arenada 72 Hz'de belirgin pay gösterirse denenir. Bedeli: kare bütçesi 13,9 → 11,1 ms, dinamik
  çözünürlük ve foveation daha sık devreye girer, gözlük daha çabuk ısınır.
- [ ] **Shader/PSO ısınması** (`GraphicsStateCollection`): arenaya ilk girişte ya da bir silahın,
  efektin ilk görünüşünde takılma §1'de görünürse — koleksiyon cihazda kaydedilir, yükleme ekranı
  sırasında ısıtılır.

## 3. Kod tarafı

- [ ] **Rastgele silah kipinde yerel silah havuzu:** `WeaponGranter.Grant` her grip basışında silahı
  `Instantiate`, bırakışta `Destroy` eder (`WeaponGrantKind.Disposable`). Takılma §1'de görünürse
  tanım başına serbest liste — ⚠️ `ThrowablePool`'un halkası değil (dolunca en eskiyi geri alır, yani
  silahı elden çeker). Yeniden kullanımda `Weapon.Awake`'in kurduğu durum (cephane →
  `RefillFull`) ve verişin kapattığı grab/fizik yolları yeniden kurulmalı.
- [ ] **Durum paketini ayrıştırırken ayırma:** snapshot (`0x05`) her pakette üç dizi, iskelet
  (`0x08`) oyuncu başına `ReadBytes` blob'u kurar. Önceden ayrılmış kazıma dizileri + sayaç ve
  blob'u tampondan doğrudan çözme (`SkeletonWire.TryRead` ofset alıyor). ⚠️ Bu kod
  `_Shared/Net/Protocol` altındadır ve sunucuya da derlenir: API değişikliği sunucu derlemesiyle
  birlikte doğrulanır.

## 4. Dokümanlar (aynı commit)

- §1: `Docs/ArenaNet-Protokol.md` (`status`), `Docs/Sistem-Ozeti.md` (`ArenaClient`, sunucunun
  telemetri logu), `Server/README.md` (log satırı).
- §2: `Docs/Sistem-Ozeti.md` → `QuestRenderSettings` satırının "bilerek dokunulmayanlar" kısmı.
