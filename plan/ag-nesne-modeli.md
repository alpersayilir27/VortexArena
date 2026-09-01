# Ağ nesnesi modeli (`NetObject`) — KALAN İŞLER

> **B1 · B2 · B3 yazıldı** (protokol **v18**). Kalıcı bilgi dokümanda: kural + kapılar + bit
> sözleşmesi + sahiplik + olaylar + dinamik doğuş `Docs/ArenaNet-Protokol.md` §10.10 · mesajlar §5.1
> ve §5.3 · obje pozu §6.12 ve §6.8 · `maps.json` şekli §11 · akış ve bileşen sorumlulukları
> `Docs/Sistem-Ozeti.md` · reçete `Yemek-Kitabi` · yasaklar `Yapma-Listesi`.
> Bu dosya yalnız **yapılmamış** olanı tutar; hepsi bitince silinir.

Modelin ilk tam tüketicisi Hamburgerci'dir: `NetSpawnCatalog` `Resources` altında, 24 tür asset'i,
dinamik ve sahne prefabları, sahne yerleşimi ve export **kuruldu**
(→ `cocuk-oyunlari-hamburgerci.md`). Yeni içeriğin nasıl kurulacağı `Yemek-Kitabi` 11.4/11.5/11.6'da.

## Doğrulama (kullanıcı koşar)

**B1 (hâlâ koşulmadı):**

- [ ] Sahneye `NetObject` + `NetObjectKind` konup sahne kaydedilince
      `Data/<SahneAdı>_objects.json` yazılıyor; obje silinince dosya siliniyor.
- [ ] `Export Server Config` → `maps.json`'da `objects[]` ve `kinds[]` görünüyor; ikinci export
      **aynı baytları** üretiyor (git diff boş).
- [ ] `SceneIdGuard` aralık dışı/çakışan kimliği onarıyor; kimlik `NET_ID_SCENE_MAX`'ı aşmıyor.
- [ ] Turnuvada tur başı sıfırlama çalışıyor.
- [ ] Hazırlık panelindeki "Ağ nesneleri" satırı eksik/yinelenen `kind`'ı yakalıyor.

**B2 — sahiplik ve poz:**

- [ ] 17+ oyuncuda fırlatılan obje uzak başlıkta zıplayarak da olsa **doğru yerde duruyor** (snapshot
      parçalanınca obje bölümü düşer, dinlenme pozu WS'ten gelir).

**B3 — olaylar ve dinamik doğuş:**

- [ ] Türün izin listesinde olmayan bir olay adı **reddediliyor** (tek satır log, sessiz).
- [ ] `owner` politikalı olay sahibi olmayandan gelince reddediliyor.
- [ ] Durumu değiştiren olay `object_state` yayınlıyor ve **aynı olay ayrıca yayınlanmıyor**
      (istemcide çift sunum yok).
- [ ] Modun doğurduğu obje herkeste aynı anda beliriyor; despawn edilen herkeste yok oluyor.
- [ ] Maç ortasında bağlanan oyuncu **o ana kadar doğmuş dinamik objeleri de** görüyor.
- [ ] Tur/sahne sıfırlaması dinamik objelerin hepsini siliyor; ikinci tur temiz başlıyor.
- [ ] Despawn edilen kimlik tur bitmeden yeniden verilmiyor.

⚠️ v18 **tüm başlıklara + admin'e + sunucuya aynı turda yeni build** ister. Karışık sürümde
`0x05`'in başlığı kaydığı için bozulan şey obje değil **snapshot'ın tamamıdır** — uzak oyuncular çöp
pozlara ışınlanır.

## 3. Bilinçli olarak almadıklarımız

- Interest management, lag compensation, tahmin/geri sarma — LAN, tek oda, ≤ 20 oyuncu.
- Yansıma/metot-bağlama RPC (`[Rpc]` attribute, metot adı telde).
- Genel amaçlı `NetworkVariable<T>` — JsonUtility'de Dictionary/polimorfizm yok; obje durumu
  **sabit şemalıdır**, anlamı `kind` verir.
- İstemcinin istediği prefabı spawn etmesi — sunucu yalnız katalogdaki `kind`'ı ve yalnız mod/tür
  kuralı izin verince spawn eder.
- Objenin telde parent'lanması; obje→obje ilişkisinin ayrı bir temsili; istemcinin yazdığı `stage`.
